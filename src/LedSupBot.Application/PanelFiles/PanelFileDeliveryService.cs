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

public enum PanelFileDeliveryOutcome { Completed, PartialFailure, Failed, Blocked, AlreadyCompleted }
public sealed record PanelFileDeliveryResult(PanelFileDeliveryOutcome Outcome, bool FirmwareSent, bool ReceiverConfigSent);

public interface IPanelFileDeliveryService
{
    Task<PanelFileDeliveryResult> DeliverAsync(TechnicalSupportCase supportCase, Guid conversationId, string customerIdentity,
        string? deliveryRoute, bool forceNewAttempt = false, CancellationToken cancellationToken = default);
}

public sealed class PanelFileDeliveryService(
    IApplicationDbContext db, IPanelFileDeliverySafetyGate gate, IMediaStorage storage,
    IWhatsAppClient client, IWhatsAppTestAllowlist allowlist, IWhatsAppSelfIdentityStore selfIdentity,
    IOptions<WorkflowOptions> workflowOptions) : IPanelFileDeliveryService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public async Task<PanelFileDeliveryResult> DeliverAsync(TechnicalSupportCase supportCase, Guid conversationId, string customerIdentity,
        string? deliveryRoute, bool forceNewAttempt = false, CancellationToken cancellationToken = default)
    {
        if (!workflowOptions.Value.AutoReplyEnabled) return new(PanelFileDeliveryOutcome.Blocked, false, false);
        if (!await CanAutoDeliverAsync(conversationId, cancellationToken))
            return new(PanelFileDeliveryOutcome.Blocked, false, false);
        var authorized = await gate.EvaluateAsync(supportCase, cancellationToken);
        if (authorized.Decision != PanelFileDeliveryDecision.Allowed || authorized.ConfirmationId is null)
            return new(PanelFileDeliveryOutcome.Blocked, false, false);

        var customerId = await db.Conversations.AsNoTracking().Where(x => x.Id == conversationId)
            .Select(x => x.CustomerId).SingleAsync(cancellationToken);
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == customerId, cancellationToken);
        var route = deliveryRoute ?? customerIdentity;
        if (WhatsAppEventMapper.IsGroupJid(route) || !allowlist.IsOutboundAllowed(route, customer))
            return new(PanelFileDeliveryOutcome.Blocked, false, false);
        var destination = await WhatsAppOutboundDestinationResolver.ResolveAsync(
            db, selfIdentity, conversationId, deliveryRoute, customerIdentity, cancellationToken);
        if (destination.SelfMatch || selfIdentity.IsSelfJid(destination.Address) ||
            WhatsAppEventMapper.IsGroupJid(destination.Address) || !allowlist.IsOutboundAllowed(destination.Address, customer))
            return new(PanelFileDeliveryOutcome.Blocked, false, false);

        var state = Deserialize(supportCase.PanelFileDeliveryStateJson);
        if (state is not null && state.ConfirmationId != authorized.ConfirmationId)
        {
            await ArchiveAsync(supportCase, state, cancellationToken);
            state = null;
        }
        if (state?.CompletedAt is not null && !forceNewAttempt)
            return new(PanelFileDeliveryOutcome.AlreadyCompleted, true, true);
        if (state is not null && forceNewAttempt)
        {
            await ArchiveAsync(supportCase, state, cancellationToken);
            state = null;
        }
        state ??= new(authorized.ConfirmationId.Value, authorized.CanonicalKey!, authorized.PackageId!.Value,
            authorized.FirmwareAssetId!.Value, authorized.FirmwareSha256!, null,
            authorized.ReceiverConfigAssetId!.Value, authorized.ReceiverConfigSha256!, null, DateTimeOffset.UtcNow);
        await PersistAsync(supportCase, state, cancellationToken);

        try
        {
            if (state.FirmwareMessageId is null)
            {
                if (!await CanAutoDeliverAsync(conversationId, cancellationToken))
                    return new(PanelFileDeliveryOutcome.Blocked, false, false);
                var id = await SendVerifiedAsync(destination.Address, state.PackageId, state.FirmwareAssetId,
                    state.FirmwareSha256, ".hex", cancellationToken);
                state = state with { FirmwareMessageId = id ?? "accepted-without-id", LastError = null };
                await PersistAsync(supportCase, state, cancellationToken);
            }
            if (state.ReceiverConfigMessageId is null)
            {
                if (!await CanAutoDeliverAsync(conversationId, cancellationToken))
                    return new(PanelFileDeliveryOutcome.PartialFailure, state.FirmwareMessageId is not null, false);
                // Revalidate after the first send so an archive/replacement stops the second asset.
                var current = await gate.EvaluateAsync(supportCase, cancellationToken);
                if (current.Decision != PanelFileDeliveryDecision.Allowed || current.ConfirmationId != state.ConfirmationId)
                    throw new InvalidOperationException("delivery_authorization_changed");
                var id = await SendVerifiedAsync(destination.Address, state.PackageId, state.ReceiverConfigAssetId,
                    state.ReceiverConfigSha256, ".rcvbp", cancellationToken);
                state = state with { ReceiverConfigMessageId = id ?? "accepted-without-id", CompletedAt = DateTimeOffset.UtcNow, LastError = null };
                await PersistAsync(supportCase, state, cancellationToken);
            }
            await ArchiveAsync(supportCase, state, cancellationToken);
            return new(PanelFileDeliveryOutcome.Completed, true, true);
        }
        catch (Exception ex)
        {
            state = state with { LastError = ex.GetType().Name };
            await PersistAsync(supportCase, state, CancellationToken.None);
            await ArchiveAsync(supportCase, state, CancellationToken.None);
            return new(state.FirmwareMessageId is not null ? PanelFileDeliveryOutcome.PartialFailure : PanelFileDeliveryOutcome.Failed,
                state.FirmwareMessageId is not null, state.ReceiverConfigMessageId is not null);
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
    private async Task<bool> CanAutoDeliverAsync(Guid conversationId, CancellationToken ct) =>
        ConversationBotPolicy.CanAutoReply(await db.Conversations.AsNoTracking().Where(x => x.Id == conversationId)
            .Select(x => x.BotMode).SingleAsync(ct));
    private async Task PersistAsync(TechnicalSupportCase supportCase, PanelFileDeliveryState state, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await db.TechnicalSupportCases.Where(x => x.Id == supportCase.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.PanelFileDeliveryStateJson, json), ct);
        supportCase.PanelFileDeliveryStateJson = json;
    }
    private async Task ArchiveAsync(TechnicalSupportCase supportCase, PanelFileDeliveryState state, CancellationToken ct)
    {
        List<PanelFileDeliveryState> history;
        try { history = JsonSerializer.Deserialize<List<PanelFileDeliveryState>>(supportCase.PanelFileDeliveryHistoryJson ?? "[]", JsonOptions) ?? []; }
        catch (JsonException) { history = []; }
        var index = history.FindIndex(x => x.ConfirmationId == state.ConfirmationId && x.StartedAt == state.StartedAt);
        if (index >= 0) history[index] = state; else history.Add(state);
        var json = JsonSerializer.Serialize(history.TakeLast(100), JsonOptions);
        await db.TechnicalSupportCases.Where(x => x.Id == supportCase.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.PanelFileDeliveryHistoryJson, json), ct);
        supportCase.PanelFileDeliveryHistoryJson = json;
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
