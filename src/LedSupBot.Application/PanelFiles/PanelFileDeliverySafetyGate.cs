using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LedSupBot.Application.Abstractions;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.PanelFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LedSupBot.Application.PanelFiles;

public enum PanelFileConfirmationStatus { Pending, Confirmed, Rejected, Invalidated }
public enum PanelFileDeliveryDecision { Allowed, Blocked }
public enum PanelFileDeliveryBlockedReason
{
    None, MissingIdentity, IdentityConflict, PackageNotFound, PackageInactive, PackageIncomplete,
    MissingFirmware, MissingReceiverConfig, ConfirmationRequired, ConfirmationRejected,
    ConfirmationStale, PackageChanged, CaseClosed, PersistenceFailure
}

public sealed record PanelFileDeliveryConfirmationState(
    Guid ConfirmationId, Guid TechnicalSupportCaseId, string CanonicalKey, Guid PackageId, string IdentityToken,
    Guid FirmwareAssetId, string FirmwareSha256, Guid ReceiverConfigAssetId, string ReceiverConfigSha256,
    DateTimeOffset RequestedAt, PanelFileConfirmationStatus Status, DateTimeOffset? ConfirmedAt = null,
    DateTimeOffset? RejectedAt = null, DateTimeOffset? InvalidatedAt = null, Guid? CustomerMessageId = null,
    string? Reason = null, PanelFileDeliveryDecision Decision = PanelFileDeliveryDecision.Blocked,
    PanelFileDeliveryBlockedReason BlockedReason = PanelFileDeliveryBlockedReason.ConfirmationRequired,
    IReadOnlyList<PanelFileAssetBinding>? Assets = null,
    bool IsFolderPackage = false);

public sealed record PanelFileAssetBinding(Guid AssetId, string Sha256, string OriginalFileName);

public sealed record PanelFileDeliveryGateResult(
    PanelFileDeliveryDecision Decision, PanelFileDeliveryBlockedReason BlockedReason,
    Guid? ConfirmationId = null, string? CanonicalKey = null, Guid? PackageId = null,
    Guid? FirmwareAssetId = null, string? FirmwareSha256 = null,
    Guid? ReceiverConfigAssetId = null, string? ReceiverConfigSha256 = null,
    IReadOnlyList<PanelFileAssetBinding>? Assets = null,
    bool IsFolderPackage = false);

public interface IPanelFileDeliverySafetyGate
{
    Task<PanelIdentityTurnResult> HandleAsync(TechnicalSupportCase supportCase, string? customerText,
        Guid customerMessageId, bool identityTurnAttempted, CancellationToken ct = default);
    Task<PanelFileDeliveryGateResult> EvaluateAsync(TechnicalSupportCase supportCase, CancellationToken ct = default);
}

public sealed partial class PanelFileDeliverySafetyGate(
    IApplicationDbContext db,
    IPanelFilePackageResolver resolver,
    ILogger<PanelFileDeliverySafetyGate> logger) : IPanelFileDeliverySafetyGate
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public async Task<PanelIdentityTurnResult> HandleAsync(TechnicalSupportCase supportCase, string? customerText,
        Guid customerMessageId, bool identityTurnAttempted, CancellationToken ct = default)
    {
        var identity = DeserializeIdentity(supportCase.PanelIdentityStateJson);
        var confirmation = DeserializeConfirmation(supportCase.PanelFileDeliveryConfirmationJson);
        if (identity is null || identity.ResolutionState == PanelIdentityResolutionState.IdentityConflict || identity.CanonicalKey is null)
        {
            if (confirmation is { Status: PanelFileConfirmationStatus.Pending or PanelFileConfirmationStatus.Confirmed })
                await InvalidateAsync(supportCase, confirmation, "identity_changed", PanelFileDeliveryBlockedReason.ConfirmationStale, ct);
            return new(false);
        }

        var current = await ResolveCurrentAsync(supportCase, identity, ct);
        var matching = confirmation is not null && Matches(confirmation, current);
        if (confirmation is { Status: PanelFileConfirmationStatus.Pending or PanelFileConfirmationStatus.Confirmed } && !matching)
        {
            confirmation = await InvalidateAsync(supportCase, confirmation, "identity_or_package_changed", PanelFileDeliveryBlockedReason.PackageChanged, ct);
            matching = false;
        }

        if (IsResume(customerText) && confirmation?.Status == PanelFileConfirmationStatus.Pending && matching)
            return Turn(identity, confirmation, $"{Display(identity)} bilgisinin doğru olup olmadığını kontrol ediyorduk. Doğru mu?");
        if (IsResume(customerText) && confirmation?.Status == PanelFileConfirmationStatus.Confirmed && matching)
        {
            var deliveryStatus = PanelFileConversationGuidance.Handle(supportCase, customerText);
            if (deliveryStatus.Attempted) return deliveryStatus;
        }

        if (IsNegative(customerText) && confirmation?.Status == PanelFileConfirmationStatus.Pending)
        {
            var rejected = confirmation with { Status = PanelFileConfirmationStatus.Rejected, RejectedAt = DateTimeOffset.UtcNow,
                CustomerMessageId = customerMessageId, Reason = "customer_rejected", Decision = PanelFileDeliveryDecision.Blocked,
                BlockedReason = PanelFileDeliveryBlockedReason.ConfirmationRejected };
            await PersistAsync(supportCase, rejected, ct);
            return Turn(identity, rejected, "Tamam, onaylamadım. Hangi bilginin yanlış olduğunu veya doğru panel kodunu paylaşır mısınız?");
        }
        if (IsUncertain(customerText) && confirmation?.Status == PanelFileConfirmationStatus.Pending)
            return Turn(identity, confirmation, "Teslimat için panel etiketini doğrulamamız gerekiyor. Etiketi kontrol edebilir veya daha net bir fotoğraf gönderebilir misiniz?");

        if (IsPositive(customerText))
        {
            if (confirmation?.Status != PanelFileConfirmationStatus.Pending || !matching) return new(false);
            if (confirmation.IsFolderPackage)
            {
                var invalid = await InvalidateAsync(supportCase, confirmation, "exact_customer_token_required",
                    PanelFileDeliveryBlockedReason.ConfirmationRequired, ct);
                return ExactFolderTokenRequired(identity, invalid);
            }
            if (IsBareAcknowledgement(customerText) &&
                !await LatestBotTurnIsConfirmationPromptAsync(supportCase.ConversationId, identity, ct))
                return new(false);
            current = await ResolveCurrentAsync(supportCase, identity, ct); // mandatory final revalidation
            if (!Matches(confirmation, current))
            {
                var stale = await InvalidateAsync(supportCase, confirmation, "package_or_assets_changed", PanelFileDeliveryBlockedReason.PackageChanged, ct);
                return Turn(identity, stale, "Paket veya dosyalar değiştiği için önceki onay artık geçerli değil. Yeniden doğrulama gerekiyor.");
            }
            var confirmed = confirmation with { Status = PanelFileConfirmationStatus.Confirmed, ConfirmedAt = DateTimeOffset.UtcNow,
                CustomerMessageId = customerMessageId, Reason = null, Decision = PanelFileDeliveryDecision.Allowed,
                BlockedReason = PanelFileDeliveryBlockedReason.None };
            await PersistAsync(supportCase, confirmed, ct);
            return Turn(identity, confirmed, "Tamam, ilgili dosyaları gönderiyorum.", true);
        }

        if (identityTurnAttempted && current.Decision == PanelFileDeliveryDecision.Blocked) return new(false);
        if (identityTurnAttempted && current.Decision == PanelFileDeliveryDecision.Allowed)
        {
            if (current.IsFolderPackage && IsExplicitPackageRequest(customerText) &&
                identity.ExactCustomerPatternMessageId == customerMessageId)
            {
                logger.LogInformation(
                    "PACKAGE_REQUEST_DETECTED canonicalKey={CanonicalKey} customerExactToken=true",
                    identity.CanonicalKey);
                var explicitlyConfirmed = new PanelFileDeliveryConfirmationState(
                    Guid.NewGuid(), supportCase.Id, identity.CanonicalKey, current.PackageId!.Value,
                    BuildIdentityToken(identity), current.FirmwareAssetId!.Value, current.FirmwareSha256!,
                    current.ReceiverConfigAssetId!.Value, current.ReceiverConfigSha256!, DateTimeOffset.UtcNow,
                    PanelFileConfirmationStatus.Confirmed, ConfirmedAt: DateTimeOffset.UtcNow,
                    CustomerMessageId: customerMessageId, Decision: PanelFileDeliveryDecision.Allowed,
                    BlockedReason: PanelFileDeliveryBlockedReason.None, Assets: current.Assets);
                await PersistAsync(supportCase, explicitlyConfirmed, ct);
                return Turn(identity, explicitlyConfirmed, "Tamam, bu klasöre ait dosyaları gönderiyorum.", true);
            }
            if (current.IsFolderPackage)
            {
                if (confirmation is { Status: PanelFileConfirmationStatus.Pending or PanelFileConfirmationStatus.Confirmed } && matching)
                    confirmation = await InvalidateAsync(supportCase, confirmation, "exact_customer_token_required",
                        PanelFileDeliveryBlockedReason.ConfirmationRequired, ct);
                return ExactFolderTokenRequired(identity, confirmation);
            }
            if (confirmation is null || confirmation.Status is PanelFileConfirmationStatus.Invalidated or PanelFileConfirmationStatus.Rejected || !matching)
            {
                confirmation = new(Guid.NewGuid(), supportCase.Id, identity.CanonicalKey, current.PackageId!.Value,
                    BuildIdentityToken(identity), current.FirmwareAssetId!.Value, current.FirmwareSha256!,
                    current.ReceiverConfigAssetId!.Value, current.ReceiverConfigSha256!, DateTimeOffset.UtcNow,
                    PanelFileConfirmationStatus.Pending, Assets: current.Assets);
                await PersistAsync(supportCase, confirmation, ct);
            }
            return Turn(identity, confirmation, $"Etiketi {Display(identity)} olarak okudum. Doğru mu?");
        }
        return new(false);
    }

    public async Task<PanelFileDeliveryGateResult> EvaluateAsync(TechnicalSupportCase supportCase, CancellationToken ct = default)
    {
        if (supportCase.Status == Domain.Enums.SupportCaseStatus.Completed)
            return Block(PanelFileDeliveryBlockedReason.CaseClosed);
        var identity = DeserializeIdentity(supportCase.PanelIdentityStateJson);
        if (identity is null || identity.CanonicalKey is null) return Block(identity?.ResolutionState == PanelIdentityResolutionState.IdentityConflict
            ? PanelFileDeliveryBlockedReason.IdentityConflict : PanelFileDeliveryBlockedReason.MissingIdentity);
        var current = await ResolveCurrentAsync(supportCase, identity, ct);
        if (current.Decision == PanelFileDeliveryDecision.Blocked) return current;
        var confirmation = DeserializeConfirmation(supportCase.PanelFileDeliveryConfirmationJson);
        if (confirmation is null) return current with { Decision = PanelFileDeliveryDecision.Blocked, BlockedReason = PanelFileDeliveryBlockedReason.ConfirmationRequired };
        if (!Matches(confirmation, current)) return current with { Decision = PanelFileDeliveryDecision.Blocked, BlockedReason = PanelFileDeliveryBlockedReason.ConfirmationStale };
        return confirmation.Status switch
        {
            PanelFileConfirmationStatus.Confirmed => current with { ConfirmationId = confirmation.ConfirmationId },
            PanelFileConfirmationStatus.Rejected => current with { Decision = PanelFileDeliveryDecision.Blocked, BlockedReason = PanelFileDeliveryBlockedReason.ConfirmationRejected },
            PanelFileConfirmationStatus.Invalidated => current with { Decision = PanelFileDeliveryDecision.Blocked, BlockedReason = PanelFileDeliveryBlockedReason.ConfirmationStale },
            _ => current with { Decision = PanelFileDeliveryDecision.Blocked, BlockedReason = PanelFileDeliveryBlockedReason.ConfirmationRequired },
        };
    }

    private async Task<PanelFileDeliveryGateResult> ResolveCurrentAsync(TechnicalSupportCase supportCase, PanelIdentityCaseState identity, CancellationToken ct)
    {
        if (supportCase.Status == Domain.Enums.SupportCaseStatus.Completed) return Block(PanelFileDeliveryBlockedReason.CaseClosed);
        if (identity.Conflicts is { Count: > 0 }) return Block(PanelFileDeliveryBlockedReason.IdentityConflict);
        var result = await resolver.ResolveAsync(new(identity.PixelPitch, identity.PanelCode, identity.SecondaryCode), ct);
        if (result.Status == PanelFilePackageResolutionStatus.IncompletePackage)
        {
            var hasHex = result.Assets?.Any(x => x.AssetType == PanelFileAssetType.FirmwareHex) == true;
            var hasRcvbp = result.Assets?.Any(x => x.AssetType == PanelFileAssetType.ReceiverConfigRcvbp) == true;
            return Block(!hasHex ? PanelFileDeliveryBlockedReason.MissingFirmware
                : !hasRcvbp ? PanelFileDeliveryBlockedReason.MissingReceiverConfig
                : PanelFileDeliveryBlockedReason.PackageIncomplete, result.PackageId, result.CanonicalKey);
        }
        if (result.Status != PanelFilePackageResolutionStatus.ExactMatch)
            return Block(result.Status switch
            {
                PanelFilePackageResolutionStatus.NoMatch => PanelFileDeliveryBlockedReason.PackageNotFound,
                PanelFilePackageResolutionStatus.InactivePackage => PanelFileDeliveryBlockedReason.PackageInactive,
                _ => PanelFileDeliveryBlockedReason.MissingIdentity,
            }, result.PackageId, result.CanonicalKey);
        var activeAssets = result.Assets?.OrderBy(x => x.RelativePath ?? x.OriginalFileName, StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        if (activeAssets.Count == 0) return Block(PanelFileDeliveryBlockedReason.PackageIncomplete, result.PackageId, result.CanonicalKey);
        var hex = activeAssets.FirstOrDefault(x => x.AssetType == PanelFileAssetType.FirmwareHex) ?? activeAssets[0];
        var rcvbp = activeAssets.FirstOrDefault(x => x.AssetType == PanelFileAssetType.ReceiverConfigRcvbp) ?? activeAssets[^1];
        return new(PanelFileDeliveryDecision.Allowed, PanelFileDeliveryBlockedReason.None, CanonicalKey: result.CanonicalKey,
            PackageId: result.PackageId, FirmwareAssetId: hex.Id, FirmwareSha256: hex.Sha256,
            ReceiverConfigAssetId: rcvbp.Id, ReceiverConfigSha256: rcvbp.Sha256,
            Assets: activeAssets.Select(x => new PanelFileAssetBinding(x.Id, x.Sha256, x.OriginalFileName)).ToList(),
            IsFolderPackage: result.IsFolderPackage);
    }

    private async Task<PanelFileDeliveryConfirmationState> InvalidateAsync(TechnicalSupportCase supportCase,
        PanelFileDeliveryConfirmationState state, string reason, PanelFileDeliveryBlockedReason blockedReason, CancellationToken ct)
    {
        var invalid = state with { Status = PanelFileConfirmationStatus.Invalidated, InvalidatedAt = DateTimeOffset.UtcNow,
            Reason = reason, Decision = PanelFileDeliveryDecision.Blocked, BlockedReason = blockedReason };
        await PersistAsync(supportCase, invalid, ct); return invalid;
    }
    private async Task PersistAsync(TechnicalSupportCase supportCase, PanelFileDeliveryConfirmationState state, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await db.TechnicalSupportCases.Where(x => x.Id == supportCase.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PanelFileDeliveryConfirmationJson, json), ct);
        supportCase.PanelFileDeliveryConfirmationJson = json;
    }
    private async Task<bool> LatestBotTurnIsConfirmationPromptAsync(
        Guid conversationId, PanelIdentityCaseState identity, CancellationToken ct)
    {
        var latestBotText = await db.Messages
            .AsNoTracking()
            .Where(x => x.ConversationId == conversationId &&
                        x.Direction == Domain.Enums.MessageDirection.Outbound &&
                        x.SentBy == Domain.Enums.MessageSender.Bot &&
                        x.MessageType == Domain.Enums.MessageType.Text)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => x.Content)
            .FirstOrDefaultAsync(ct);

        return latestBotText is not null &&
               latestBotText.Contains(Display(identity), StringComparison.OrdinalIgnoreCase) &&
               latestBotText.Contains("doğru mu", StringComparison.OrdinalIgnoreCase);
    }
    private static bool Matches(PanelFileDeliveryConfirmationState state, PanelFileDeliveryGateResult current) =>
        current.Decision == PanelFileDeliveryDecision.Allowed && state.PackageId == current.PackageId &&
        string.Equals(state.CanonicalKey, current.CanonicalKey, StringComparison.Ordinal) && state.FirmwareAssetId == current.FirmwareAssetId &&
        state.ReceiverConfigAssetId == current.ReceiverConfigAssetId && string.Equals(state.FirmwareSha256, current.FirmwareSha256, StringComparison.Ordinal) &&
        string.Equals(state.ReceiverConfigSha256, current.ReceiverConfigSha256, StringComparison.Ordinal) &&
        AssetBindingsMatch(state.Assets, current.Assets);
    private static bool AssetBindingsMatch(IReadOnlyList<PanelFileAssetBinding>? left, IReadOnlyList<PanelFileAssetBinding>? right)
    {
        if (left is null) return true; // compatibility with confirmations written before N-file delivery
        if (right is null || left.Count != right.Count) return false;
        return left.Zip(right).All(x => x.First.AssetId == x.Second.AssetId &&
            string.Equals(x.First.Sha256, x.Second.Sha256, StringComparison.OrdinalIgnoreCase));
    }
    private static string BuildIdentityToken(PanelIdentityCaseState state) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{state.CanonicalKey}|{state.PixelPitchMessageId}|{state.PanelCodeMessageId}|{state.SecondaryCodeMessageId}|{state.UpdatedAt:O}"))).ToLowerInvariant();
    private static PanelIdentityTurnResult Turn(PanelIdentityCaseState identity, PanelFileDeliveryConfirmationState confirmation, string reply, bool deliveryAuthorizedNow = false) =>
        new(true, reply, JsonSerializer.Serialize(new { panelIdentityAttempted = true, panelIdentityCanonicalKey = identity.CanonicalKey,
            packageResolutionStatus = identity.PackageResolution, packageId = identity.PackageId, confirmationRequested = true,
            confirmationStatus = confirmation.Status, confirmationTargetCanonicalKey = confirmation.CanonicalKey,
            confirmationTargetPackageId = confirmation.PackageId, deliveryGateDecision = confirmation.Decision,
            deliveryGateBlockedReason = confirmation.BlockedReason, identity.PixelPitchSource, identity.PanelCodeSource, identity.SecondaryCodeSource }, JsonOptions), deliveryAuthorizedNow);
    private static PanelIdentityTurnResult ExactFolderTokenRequired(
        PanelIdentityCaseState identity, PanelFileDeliveryConfirmationState? confirmation) =>
        new(true, $"Dosyaları gönderebilmem için kodu şu formatta gönder: {identity.PixelPitch}+{identity.PanelCode}+{identity.SecondaryCode}",
            JsonSerializer.Serialize(new { panelIdentityAttempted = true, panelIdentityCanonicalKey = identity.CanonicalKey,
                packageResolutionStatus = identity.PackageResolution, packageId = identity.PackageId,
                confirmationStatus = confirmation?.Status, deliveryGateDecision = PanelFileDeliveryDecision.Blocked,
                deliveryGateBlockedReason = PanelFileDeliveryBlockedReason.ConfirmationRequired,
                exactCustomerTokenRequired = true }, JsonOptions));
    private static PanelFileDeliveryGateResult Block(PanelFileDeliveryBlockedReason reason, Guid? packageId = null, string? key = null) =>
        new(PanelFileDeliveryDecision.Blocked, reason, CanonicalKey: key, PackageId: packageId);
    private static string Display(PanelIdentityCaseState state) => $"{state.PixelPitch} + {state.PanelCode} + {state.SecondaryCode}";
    private static bool IsPositive(string? text) => PositiveRegex().IsMatch(text ?? "");
    private static bool IsBareAcknowledgement(string? text) => BareAcknowledgementRegex().IsMatch(text ?? "");
    private static bool IsNegative(string? text) => NegativeRegex().IsMatch(text ?? "");
    private static bool IsUncertain(string? text) => UncertainRegex().IsMatch(text ?? "");
    private static bool IsResume(string? text) => ResumeRegex().IsMatch(text ?? "");
    private static bool IsExplicitPackageRequest(string? text)
    {
        var value = text?.Trim() ?? "";
        var token = ExplicitPackageTokenRegex().Match(value);
        if (!token.Success) return false;
        return token.Length == value.Length || PackageDeliveryIntentRegex().IsMatch(value);
    }
    private static PanelIdentityCaseState? DeserializeIdentity(string? json) { try { return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<PanelIdentityCaseState>(json, JsonOptions); } catch (JsonException) { return null; } }
    private static PanelFileDeliveryConfirmationState? DeserializeConfirmation(string? json) { try { return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<PanelFileDeliveryConfirmationState>(json, JsonOptions); } catch (JsonException) { return null; } }
    private static JsonSerializerOptions CreateOptions() { var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); options.Converters.Add(new JsonStringEnumConverter()); return options; }
    [GeneratedRegex(@"^\s*(?:evet|doğru|dogru|aynen|bu\s+doğru|bu\s+dogru|tamam\s+doğru|tamam\s+dogru|tamam)\s*[.!]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)] private static partial Regex PositiveRegex();
    [GeneratedRegex(@"^\s*tamam\s*[.!]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)] private static partial Regex BareAcknowledgementRegex();
    [GeneratedRegex(@"^\s*(?:hayır|hayir)\s*[.!]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)] private static partial Regex NegativeRegex();
    [GeneratedRegex(@"^\s*bilmiyorum\s*[.!]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)] private static partial Regex UncertainRegex();
    [GeneratedRegex(@"\bnerede\s+kalm[ıi]şt[ıi]k\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)] private static partial Regex ResumeRegex();
    [GeneratedRegex(@"(?<![A-Z0-9])P\d+(?:[.,]\d+)?\+[A-Z0-9-]+\+[A-Z0-9-]+(?![A-Z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex ExplicitPackageTokenRegex();
    [GeneratedRegex(@"\b(?:dosya(?:lar(?:ı|i|ını|ini)?)?|tarama(?:sı|si|sını|sini)?|gönder(?:ir\s*misin(?:iz)?|ebilir\s*misin(?:iz)?|in)?|gonder(?:ir\s*misin(?:iz)?|ebilir\s*misin(?:iz)?|in)?|at(?:ar\s*mısın(?:ız)?|ar\s*misin(?:iz)?)?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)] private static partial Regex PackageDeliveryIntentRegex();
}
