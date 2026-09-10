using System.Security.Cryptography;
using System.Collections.Concurrent;
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
    DateTimeOffset StartedAt, DateTimeOffset? CompletedAt = null, string? LastError = null,
    IReadOnlyList<PanelFileDeliveryAssetState>? Assets = null);
public sealed record PanelFileDeliveryAssetState(
    Guid AssetId, string Sha256, string OriginalFileName, string? MessageId = null,
    int AttemptCount = 0, string? FailureReason = null);

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
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> DeliveryLocks = new();

    public async Task<PanelFileDeliveryResult> DeliverAsync(TechnicalSupportCase supportCase, Guid conversationId, string customerIdentity,
        string? deliveryRoute, bool forceNewAttempt = false, CancellationToken cancellationToken = default)
    {
        var deliveryLock = DeliveryLocks.GetOrAdd(supportCase.Id, static _ => new SemaphoreSlim(1, 1));
        await deliveryLock.WaitAsync(cancellationToken);
        try
        {
            return await DeliverCoreAsync(supportCase, conversationId, customerIdentity, deliveryRoute, forceNewAttempt, cancellationToken);
        }
        finally
        {
            deliveryLock.Release();
        }
    }

    private async Task<PanelFileDeliveryResult> DeliverCoreAsync(TechnicalSupportCase supportCase, Guid conversationId, string customerIdentity,
        string? deliveryRoute, bool forceNewAttempt, CancellationToken cancellationToken)
    {
        if (!workflowOptions.Value.AutoReplyEnabled) return new(PanelFileDeliveryOutcome.Blocked, false, false);
        if (!await CanAutoDeliverAsync(conversationId, cancellationToken))
            return new(PanelFileDeliveryOutcome.Blocked, false, false);
        supportCase = await LoadAuthoritativeCaseAsync(supportCase.Id, cancellationToken);
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

        supportCase.PanelFileDeliveryStateJson = await db.TechnicalSupportCases.AsNoTracking()
            .Where(x => x.Id == supportCase.Id).Select(x => x.PanelFileDeliveryStateJson).SingleAsync(cancellationToken);
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
            authorized.ReceiverConfigAssetId!.Value, authorized.ReceiverConfigSha256!, null, DateTimeOffset.UtcNow,
            Assets: authorized.Assets?.Select(x => new PanelFileDeliveryAssetState(x.AssetId, x.Sha256, x.OriginalFileName)).ToList());
        var deliveryAssets = NormalizeAssets(state);
        await PersistAsync(supportCase, state, cancellationToken);

        try
        {
            foreach (var pending in deliveryAssets.Where(x => x.MessageId is null).ToList())
            {
                if (!await CanAutoDeliverAsync(conversationId, cancellationToken))
                    return BuildResult(PanelFileDeliveryOutcome.Blocked, deliveryAssets);
                if (!await AuthorizationStillMatchesAsync(supportCase.Id, state, cancellationToken))
                    return BuildResult(PanelFileDeliveryOutcome.Blocked, deliveryAssets);
                var index = deliveryAssets.FindIndex(x => x.AssetId == pending.AssetId);
                try
                {
                    var id = await SendVerifiedAsync(destination.Address, state.PackageId, pending.AssetId,
                        pending.Sha256, cancellationToken);
                    deliveryAssets[index] = pending with { MessageId = id ?? "accepted-without-id", AttemptCount = pending.AttemptCount + 1, FailureReason = null };
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    deliveryAssets[index] = pending with { AttemptCount = pending.AttemptCount + 1, FailureReason = ex.GetType().Name };
                    state = ApplyAssets(state, deliveryAssets, ex.GetType().Name);
                    await PersistAsync(supportCase, state, CancellationToken.None);
                    await ArchiveAsync(supportCase, state, CancellationToken.None);
                    return BuildResult(deliveryAssets.Any(x => x.MessageId is not null)
                        ? PanelFileDeliveryOutcome.PartialFailure : PanelFileDeliveryOutcome.Failed, deliveryAssets);
                }
                state = ApplyAssets(state, deliveryAssets, null);
                await PersistAsync(supportCase, state, cancellationToken);
            }
            state = ApplyAssets(state, deliveryAssets, null) with { CompletedAt = DateTimeOffset.UtcNow };
            await PersistAsync(supportCase, state, cancellationToken);
            await ArchiveAsync(supportCase, state, cancellationToken);
            return BuildResult(PanelFileDeliveryOutcome.Completed, deliveryAssets);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            state = state with { LastError = ex.GetType().Name };
            await PersistAsync(supportCase, state, CancellationToken.None);
            await ArchiveAsync(supportCase, state, CancellationToken.None);
            return BuildResult(deliveryAssets.Any(x => x.MessageId is not null)
                ? PanelFileDeliveryOutcome.PartialFailure : PanelFileDeliveryOutcome.Failed, deliveryAssets);
        }
    }

    private async Task<string?> SendVerifiedAsync(string destination, Guid packageId, Guid assetId,
        string expectedHash, CancellationToken ct)
    {
        var asset = await db.PanelFilePackageAssets.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == assetId && x.PanelFilePackageId == packageId && x.Status == Domain.PanelFiles.PanelFileAssetStatus.Active, ct)
            ?? throw new InvalidOperationException("bound_asset_unavailable");
        if (Path.GetFileName(asset.OriginalFileName) != asset.OriginalFileName ||
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
        type == Domain.PanelFiles.PanelFileAssetType.FirmwareHex ? "Firmware" :
        type == Domain.PanelFiles.PanelFileAssetType.ReceiverConfigRcvbp ? "Alıcı kartı ayarı" : "Panel paket dosyası";
    private async Task<bool> CanAutoDeliverAsync(Guid conversationId, CancellationToken ct) =>
        ConversationBotPolicy.CanAutoReply(await db.Conversations.AsNoTracking().Where(x => x.Id == conversationId)
            .Select(x => x.BotMode).SingleAsync(ct));
    private Task<TechnicalSupportCase> LoadAuthoritativeCaseAsync(Guid caseId, CancellationToken ct) =>
        db.TechnicalSupportCases.AsNoTracking().SingleAsync(x => x.Id == caseId, ct);
    private async Task<bool> AuthorizationStillMatchesAsync(Guid caseId, PanelFileDeliveryState state, CancellationToken ct)
    {
        var current = await gate.EvaluateAsync(await LoadAuthoritativeCaseAsync(caseId, ct), ct);
        return current.Decision == PanelFileDeliveryDecision.Allowed && current.ConfirmationId == state.ConfirmationId &&
            current.PackageId == state.PackageId && current.FirmwareAssetId == state.FirmwareAssetId &&
            current.ReceiverConfigAssetId == state.ReceiverConfigAssetId &&
            string.Equals(current.FirmwareSha256, state.FirmwareSha256, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(current.ReceiverConfigSha256, state.ReceiverConfigSha256, StringComparison.OrdinalIgnoreCase) &&
            (state.Assets is null || current.Assets is not null && state.Assets.Count == current.Assets.Count &&
             state.Assets.All(x => current.Assets.Any(y => y.AssetId == x.AssetId && string.Equals(y.Sha256, x.Sha256, StringComparison.OrdinalIgnoreCase))));
    }
    private static List<PanelFileDeliveryAssetState> NormalizeAssets(PanelFileDeliveryState state) => state.Assets?.ToList() ??
    [
        new(state.FirmwareAssetId, state.FirmwareSha256, "firmware.hex", state.FirmwareMessageId),
        new(state.ReceiverConfigAssetId, state.ReceiverConfigSha256, "config.rcvbp", state.ReceiverConfigMessageId)
    ];
    private static PanelFileDeliveryState ApplyAssets(PanelFileDeliveryState state, IReadOnlyList<PanelFileDeliveryAssetState> assets, string? error)
    {
        var firmware = assets.FirstOrDefault(x => x.AssetId == state.FirmwareAssetId);
        var config = assets.FirstOrDefault(x => x.AssetId == state.ReceiverConfigAssetId);
        return state with { Assets = assets.ToList(), FirmwareMessageId = firmware?.MessageId,
            ReceiverConfigMessageId = config?.MessageId, LastError = error };
    }
    private static PanelFileDeliveryResult BuildResult(PanelFileDeliveryOutcome outcome, IReadOnlyList<PanelFileDeliveryAssetState> assets) =>
        new(outcome,
            assets.Any(x => Path.GetExtension(x.OriginalFileName).Equals(".hex", StringComparison.OrdinalIgnoreCase) && x.MessageId is not null),
            assets.Any(x => Path.GetExtension(x.OriginalFileName).Equals(".rcvbp", StringComparison.OrdinalIgnoreCase) && x.MessageId is not null));
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
