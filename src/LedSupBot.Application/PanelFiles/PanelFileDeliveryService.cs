using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.WhatsApp;
using LedSupBot.Application.Workflow;
using LedSupBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LedSupBot.Application.PanelFiles;

public sealed record PanelFileDeliveryState(
    Guid ConfirmationId, string CanonicalKey, Guid PackageId,
    Guid FirmwareAssetId, string FirmwareSha256, string? FirmwareMessageId,
    Guid ReceiverConfigAssetId, string ReceiverConfigSha256, string? ReceiverConfigMessageId,
    DateTimeOffset StartedAt, DateTimeOffset? CompletedAt = null, string? LastError = null);

public interface IPanelFileDeliveryService
{
    Task DeliverAsync(TechnicalSupportCase supportCase, Guid conversationId, string customerIdentity,
        string? deliveryRoute, CancellationToken cancellationToken = default);
}

public sealed class PanelFileDeliveryService(
    IApplicationDbContext db, IPanelFileDeliverySafetyGate gate, IMediaStorage storage,
    IWhatsAppClient client, IWhatsAppTestAllowlist allowlist, IWhatsAppSelfIdentityStore selfIdentity,
    IOptions<WorkflowOptions> workflowOptions) : IPanelFileDeliveryService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public async Task DeliverAsync(TechnicalSupportCase supportCase, Guid conversationId, string customerIdentity,
        string? deliveryRoute, CancellationToken cancellationToken = default)
    {
        if (!workflowOptions.Value.AutoReplyEnabled) return;
        var authorized = await gate.EvaluateAsync(supportCase, cancellationToken);
        if (authorized.Decision != PanelFileDeliveryDecision.Allowed || authorized.ConfirmationId is null) return;

        var customerId = await db.Conversations.AsNoTracking().Where(x => x.Id == conversationId)
            .Select(x => x.CustomerId).SingleAsync(cancellationToken);
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == customerId, cancellationToken);
        var route = deliveryRoute ?? customerIdentity;
        if (WhatsAppEventMapper.IsGroupJid(route) || !allowlist.IsOutboundAllowed(route, customer)) return;
        var destination = await WhatsAppOutboundDestinationResolver.ResolveAsync(
            db, selfIdentity, conversationId, deliveryRoute, customerIdentity, cancellationToken);
        if (destination.SelfMatch || selfIdentity.IsSelfJid(destination.Address) ||
            WhatsAppEventMapper.IsGroupJid(destination.Address) || !allowlist.IsOutboundAllowed(destination.Address, customer)) return;

        var state = Deserialize(supportCase.PanelFileDeliveryStateJson);
        if (state is not null && state.ConfirmationId != authorized.ConfirmationId) state = null;
        state ??= new(authorized.ConfirmationId.Value, authorized.CanonicalKey!, authorized.PackageId!.Value,
            authorized.FirmwareAssetId!.Value, authorized.FirmwareSha256!, null,
            authorized.ReceiverConfigAssetId!.Value, authorized.ReceiverConfigSha256!, null, DateTimeOffset.UtcNow);
        await PersistAsync(supportCase, state, cancellationToken);

        try
        {
            if (state.FirmwareMessageId is null)
            {
                var id = await SendVerifiedAsync(destination.Address, state.PackageId, state.FirmwareAssetId,
                    state.FirmwareSha256, ".hex", cancellationToken);
                state = state with { FirmwareMessageId = id ?? "accepted-without-id", LastError = null };
                await PersistAsync(supportCase, state, cancellationToken);
            }
            if (state.ReceiverConfigMessageId is null)
            {
                // Revalidate after the first send so an archive/replacement stops the second asset.
                var current = await gate.EvaluateAsync(supportCase, cancellationToken);
                if (current.Decision != PanelFileDeliveryDecision.Allowed || current.ConfirmationId != state.ConfirmationId)
                    throw new InvalidOperationException("delivery_authorization_changed");
                var id = await SendVerifiedAsync(destination.Address, state.PackageId, state.ReceiverConfigAssetId,
                    state.ReceiverConfigSha256, ".rcvbp", cancellationToken);
                state = state with { ReceiverConfigMessageId = id ?? "accepted-without-id", CompletedAt = DateTimeOffset.UtcNow, LastError = null };
                await PersistAsync(supportCase, state, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            state = state with { LastError = ex.GetType().Name };
            await PersistAsync(supportCase, state, CancellationToken.None);
            throw;
        }
    }

    private async Task<string?> SendVerifiedAsync(string destination, Guid packageId, Guid assetId,
        string expectedHash, string requiredExtension, CancellationToken ct)
    {
        var asset = await db.PanelFilePackageAssets.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == assetId && x.PanelFilePackageId == packageId && x.Status == Domain.PanelFiles.PanelFileAssetStatus.Active, ct)
            ?? throw new InvalidOperationException("bound_asset_unavailable");
        if (!Path.GetExtension(asset.OriginalFileName).Equals(requiredExtension, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(asset.Sha256, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("bound_asset_changed");
        await using var source = await storage.OpenReadAsync(asset.StorageKey, ct);
        using var content = new MemoryStream();
        await source.CopyToAsync(content, ct);
        var actualHash = Convert.ToHexString(SHA256.HashData(content.ToArray())).ToLowerInvariant();
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("asset_integrity_failed");
        content.Position = 0;
        return await client.SendDocumentMessageAsync(destination, content, asset.MimeType,
            Path.GetFileName(asset.OriginalFileName), $"Panel dosyası: {stateSafe(asset.AssetType)}", ct);
    }

    private static string stateSafe(Domain.PanelFiles.PanelFileAssetType type) =>
        type == Domain.PanelFiles.PanelFileAssetType.FirmwareHex ? "Firmware" : "Alıcı kartı ayarı";
    private async Task PersistAsync(TechnicalSupportCase supportCase, PanelFileDeliveryState state, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await db.TechnicalSupportCases.Where(x => x.Id == supportCase.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.PanelFileDeliveryStateJson, json), ct);
        supportCase.PanelFileDeliveryStateJson = json;
    }
    private static PanelFileDeliveryState? Deserialize(string? json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<PanelFileDeliveryState>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }
    private static JsonSerializerOptions CreateOptions()
    {
        var result = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        result.Converters.Add(new JsonStringEnumConverter());
        return result;
    }
}
