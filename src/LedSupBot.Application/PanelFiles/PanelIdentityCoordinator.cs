using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Media;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.PanelFiles;
using Microsoft.EntityFrameworkCore;

namespace LedSupBot.Application.PanelFiles;

public interface IPanelIdentityCoordinator
{
    Task<PanelIdentityTurnResult> ApplyTextAsync(TechnicalSupportCase supportCase, string? text, Guid sourceMessageId, CancellationToken ct = default);
    Task<PanelIdentityTurnResult> ApplyPhotoAsync(TechnicalSupportCase supportCase, ValidatedImageAnalysis? image, Guid sourceMessageId, CancellationToken ct = default);
}

public sealed partial class PanelIdentityCoordinator(
    IApplicationDbContext db,
    IPanelIdentityTextParser parser,
    IPanelIdentityNormalizer normalizer,
    IPanelFilePackageResolver resolver) : IPanelIdentityCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public async Task<PanelIdentityTurnResult> ApplyTextAsync(TechnicalSupportCase supportCase, string? text, Guid sourceMessageId, CancellationToken ct = default)
    {
        var parsed = parser.Parse(text);
        var prior = Deserialize(supportCase.PanelIdentityStateJson);
        if (parsed.Status == PanelIdentityTextStatus.NoIdentity)
        {
            if (prior is not null && ResumeRegex().IsMatch(text ?? ""))
            {
                if (prior.Conflicts is { Count: > 0 }) return Snapshot(prior, BuildConflictReply(prior.Conflicts), true);
                if (prior.CanonicalKey is not null) return Snapshot(prior, $"Panel kimliği {prior.PixelPitch} + {prior.PanelCode} + {prior.SecondaryCode} olarak doğrulandı.", true);
                var missing = prior.PixelPitch is null ? "Pitch" : prior.PanelCode is null ? "Panel kodu" : "Son kod";
                return Snapshot(prior, $"Panel kimliği için {missing} bilgisini bekliyorduk.", true);
            }
            if (prior is { ResolutionState: PanelIdentityResolutionState.IdentityConflict } && CorrectCandidateRegex().Match(text ?? "") is { Success: true } answer)
            {
                var value = answer.Groups[1].Value.ToUpperInvariant();
                var conflict = prior.Conflicts?.SingleOrDefault(x => string.Equals(x.CandidateValue, value, StringComparison.OrdinalIgnoreCase));
                if (conflict is not null) return await ApplyCorrectionAsync(supportCase, prior, conflict.Field, value, sourceMessageId, ct);
            }
            if (prior is not null && prior.PixelPitch is not null && prior.PanelCode is not null && prior.SecondaryCode is null && IsIsolatedSecondary(text))
                return await MergeAsync(supportCase, prior, null, null, text!.Trim().ToUpperInvariant(), PanelIdentitySource.CustomerNaturalText, sourceMessageId, "customer_pending_field", ct);
            return new(false);
        }
        if (parsed.Status == PanelIdentityTextStatus.PhotoIdentityRetraction) return await RetractAsync(supportCase, prior, photo: true, ct);
        if (parsed.Status == PanelIdentityTextStatus.TextIdentityRetraction) return await RetractAsync(supportCase, prior, photo: false, ct);
        if (parsed.Status is PanelIdentityTextStatus.AmbiguousIdentity or PanelIdentityTextStatus.InvalidIdentity)
            return Snapshot(prior, "Panel kimliği net değil. P2.5+1065S+2012 formatında tekrar yazar mısınız?", true);
        if (parsed.Source is PanelIdentitySource.CustomerCorrection or PanelIdentitySource.OperatorCorrection && parsed.CorrectedField is { } field)
        {
            var value = GetField(parsed.Identity, field);
            return prior is null || value is null
                ? Snapshot(prior, "Önce tam panel kodunu P2.5+1065S+2012 formatında paylaşır mısınız?", true)
                : await ApplyCorrectionAsync(supportCase, prior, field, value, sourceMessageId, ct);
        }
        var identity = parsed.Identity;
        // Only a fresh, literal uppercase P...+...+... customer token is authoritative.
        // Informational candidates may be retained, but can never trigger package lookup.
        var mergeBase = parsed.HasExactCustomerPattern ? null : prior;
        return await MergeAsync(supportCase, mergeBase, identity?.PixelPitch, identity?.PanelCode, identity?.SecondaryCode,
            parsed.Source ?? PanelIdentitySource.CustomerPattern, sourceMessageId, parsed.Evidence, ct,
            exactCustomerPattern: parsed.HasExactCustomerPattern);
    }

    public async Task<PanelIdentityTurnResult> ApplyPhotoAsync(TechnicalSupportCase supportCase, ValidatedImageAnalysis? image, Guid sourceMessageId, CancellationToken ct = default)
    {
        var observation = image?.Analysis.PanelIdentityObservation;
        if (observation is null) return new(false);
        var prior = Deserialize(supportCase.PanelIdentityStateJson);
        if (observation.RecognitionStatus is PanelLabelRecognitionStatus.Unreadable or PanelLabelRecognitionStatus.ProviderFailure)
            return Snapshot(prior, "Etiketteki kodlar net görünmüyor. Biraz daha yakından ve ışık yansımadan tekrar çekebilir misiniz?", true, observation);
        if (observation.RecognitionStatus == PanelLabelRecognitionStatus.MultipleLabels)
            return Snapshot(prior, "Fotoğrafta birden fazla etiket görünüyor. İlgili tek etiketi yakından gönderir misiniz?", true, observation);
        if (observation.RecognitionStatus is PanelLabelRecognitionStatus.Ambiguous or PanelLabelRecognitionStatus.ConflictingEvidence)
            return Snapshot(prior, "Son kod net görünmüyor. Etiketi biraz daha yakından tekrar gönderebilir misiniz?", true, observation);
        if (observation.PixelPitchCandidate is not null && observation.PanelCodeCandidate is not null && observation.SecondaryCodeCandidate is not null &&
            Math.Min(observation.PixelPitchConfidence, Math.Min(observation.PanelCodeConfidence, observation.SecondaryCodeConfidence)) < .78)
            return Snapshot(prior, "Etiketteki kodlardan biri yeterince net görünmüyor. Etiketi biraz daha yakından tekrar gönderebilir misiniz?", true, observation);
        var source = observation.OcrUsed && observation.VisionUsed ? PanelIdentitySource.OcrAndVision
            : observation.VisionUsed ? PanelIdentitySource.PanelLabelVision : PanelIdentitySource.PanelLabelOcr;
        return await MergeAsync(supportCase, prior, observation.PixelPitchCandidate, observation.PanelCodeCandidate,
            observation.SecondaryCodeCandidate, source, sourceMessageId, observation.Evidence, ct, observation);
    }

    private async Task<PanelIdentityTurnResult> MergeAsync(TechnicalSupportCase supportCase, PanelIdentityCaseState? prior,
        string? pitch, string? panel, string? secondary, PanelIdentitySource source, Guid sourceMessageId, string? evidence,
        CancellationToken ct, PanelLabelIdentityObservation? observation = null, bool exactCustomerPattern = false)
    {
        pitch = NormalizePitch(pitch); panel = NormalizeToken(panel); secondary = NormalizeToken(secondary);
        var conflicts = new List<PanelIdentityConflict>(prior?.Conflicts ?? []);
        AddConflict(conflicts, PanelIdentityField.PixelPitch, prior?.PixelPitch, prior?.PixelPitchSource ?? prior?.Source, pitch, source, sourceMessageId);
        AddConflict(conflicts, PanelIdentityField.PanelCode, prior?.PanelCode, prior?.PanelCodeSource ?? prior?.Source, panel, source, sourceMessageId);
        AddConflict(conflicts, PanelIdentityField.SecondaryCode, prior?.SecondaryCode, prior?.SecondaryCodeSource ?? prior?.Source, secondary, source, sourceMessageId);
        if (conflicts.Count > 0)
        {
            var conflicted = new PanelIdentityCaseState(prior?.PixelPitch, prior?.PanelCode, prior?.SecondaryCode, null,
                prior?.Source ?? source, "IdentityConflict", null, DateTimeOffset.UtcNow,
                prior?.PixelPitchSource, prior?.PanelCodeSource, prior?.SecondaryCodeSource, sourceMessageId,
                PanelIdentityResolutionState.IdentityConflict, conflicts, prior?.PixelPitchMessageId, prior?.PanelCodeMessageId, prior?.SecondaryCodeMessageId);
            await PersistAsync(supportCase, conflicted, ct);
            return Snapshot(conflicted, BuildConflictReply(conflicts), true, observation);
        }

        var mergedPitch = prior?.PixelPitch ?? pitch;
        var mergedPanel = prior?.PanelCode ?? panel;
        var mergedSecondary = prior?.SecondaryCode ?? secondary;
        PanelIdentitySource? pitchSource = prior?.PixelPitch is not null ? prior.PixelPitchSource ?? prior.Source : pitch is null ? null : source;
        PanelIdentitySource? panelSource = prior?.PanelCode is not null ? prior.PanelCodeSource ?? prior.Source : panel is null ? null : source;
        PanelIdentitySource? secondarySource = prior?.SecondaryCode is not null ? prior.SecondaryCodeSource ?? prior.Source : secondary is null ? null : source;
        var pitchMessage = prior?.PixelPitch is not null ? prior.PixelPitchMessageId ?? prior.SourceMessageId : pitch is null ? null : sourceMessageId;
        var panelMessage = prior?.PanelCode is not null ? prior.PanelCodeMessageId ?? prior.SourceMessageId : panel is null ? null : sourceMessageId;
        var secondaryMessage = prior?.SecondaryCode is not null ? prior.SecondaryCodeMessageId ?? prior.SourceMessageId : secondary is null ? null : sourceMessageId;

        var candidate = new PanelIdentityCaseState(mergedPitch, mergedPanel, mergedSecondary, null,
            DistinctSources(pitchSource, panelSource, secondarySource) > 1 ? PanelIdentitySource.MixedContext : source,
            "NotAttempted", null, DateTimeOffset.UtcNow, pitchSource, panelSource, secondarySource, sourceMessageId,
            mergedPitch is not null && mergedPanel is not null && mergedSecondary is not null ? PanelIdentityResolutionState.Candidate : PanelIdentityResolutionState.MissingInformation,
            [], pitchMessage, panelMessage, secondaryMessage, exactCustomerPattern ? sourceMessageId : prior?.ExactCustomerPatternMessageId);
        await PersistAsync(supportCase, candidate, ct); // stale package is invalidated before any new lookup
        return await ResolveOrClarifyAsync(supportCase, candidate, ct, observation);
    }

    private async Task<PanelIdentityTurnResult> ApplyCorrectionAsync(TechnicalSupportCase supportCase, PanelIdentityCaseState prior,
        PanelIdentityField field, string value, Guid sourceMessageId, CancellationToken ct)
    {
        value = field == PanelIdentityField.PixelPitch ? NormalizePitch(value)! : NormalizeToken(value)!;
        var state = prior with
        {
            PixelPitch = field == PanelIdentityField.PixelPitch ? value : prior.PixelPitch,
            PanelCode = field == PanelIdentityField.PanelCode ? value : prior.PanelCode,
            SecondaryCode = field == PanelIdentityField.SecondaryCode ? value : prior.SecondaryCode,
            PixelPitchSource = field == PanelIdentityField.PixelPitch ? PanelIdentitySource.CustomerCorrection : prior.PixelPitchSource,
            PanelCodeSource = field == PanelIdentityField.PanelCode ? PanelIdentitySource.CustomerCorrection : prior.PanelCodeSource,
            SecondaryCodeSource = field == PanelIdentityField.SecondaryCode ? PanelIdentitySource.CustomerCorrection : prior.SecondaryCodeSource,
            PixelPitchMessageId = field == PanelIdentityField.PixelPitch ? sourceMessageId : prior.PixelPitchMessageId,
            PanelCodeMessageId = field == PanelIdentityField.PanelCode ? sourceMessageId : prior.PanelCodeMessageId,
            SecondaryCodeMessageId = field == PanelIdentityField.SecondaryCode ? sourceMessageId : prior.SecondaryCodeMessageId,
            CanonicalKey = null, PackageId = null, PackageResolution = "NotAttempted", Source = PanelIdentitySource.CustomerCorrection,
            ResolutionState = PanelIdentityResolutionState.Candidate,
            ExactCustomerPatternMessageId = null,
            Conflicts = (prior.Conflicts ?? []).Where(x => x.Field != field).ToList(), UpdatedAt = DateTimeOffset.UtcNow,
        };
        if (state.Conflicts.Count > 0) state = state with { ResolutionState = PanelIdentityResolutionState.IdentityConflict, PackageResolution = "IdentityConflict" };
        await PersistAsync(supportCase, state, ct);
        return state.ResolutionState == PanelIdentityResolutionState.IdentityConflict
            ? Snapshot(state, BuildConflictReply(state.Conflicts), true)
            : await ResolveOrClarifyAsync(supportCase, state, ct);
    }

    private async Task<PanelIdentityTurnResult> RetractAsync(TechnicalSupportCase supportCase, PanelIdentityCaseState? prior, bool photo, CancellationToken ct)
    {
        if (prior is null) return new(false);
        bool Remove(PanelIdentitySource? source) => photo ? IsPhoto(source) : IsText(source);
        var pitch = Remove(prior.PixelPitchSource ?? prior.Source) ? null : prior.PixelPitch;
        var panel = Remove(prior.PanelCodeSource ?? prior.Source) ? null : prior.PanelCode;
        var secondary = Remove(prior.SecondaryCodeSource ?? prior.Source) ? null : prior.SecondaryCode;
        var remainingConflicts = (prior.Conflicts ?? []).Where(x => !Remove(x.CandidateSource)).ToList();
        var state = prior with { PixelPitch = pitch, PanelCode = panel, SecondaryCode = secondary, CanonicalKey = null, PackageId = null,
            PackageResolution = remainingConflicts.Count > 0 ? "IdentityConflict" : "NotAttempted", Conflicts = remainingConflicts,
            ResolutionState = remainingConflicts.Count > 0 ? PanelIdentityResolutionState.IdentityConflict : PanelIdentityResolutionState.MissingInformation,
            ExactCustomerPatternMessageId = null,
            UpdatedAt = DateTimeOffset.UtcNow };
        await PersistAsync(supportCase, state, ct);
        if (remainingConflicts.Count == 0 && pitch is not null && panel is not null && secondary is not null)
            return await ResolveOrClarifyAsync(supportCase, state with { ResolutionState = PanelIdentityResolutionState.Candidate }, ct);
        var reply = photo ? "Yanlış fotoğraftan gelen bilgileri geçersiz kıldım. Eksik panel bilgisini paylaşır mısınız?"
            : "Önceki yazılı panel bilgisini geçersiz kıldım. Doğru kodu paylaşır mısınız?";
        return Snapshot(state, reply, true);
    }

    private async Task<PanelIdentityTurnResult> ResolveOrClarifyAsync(TechnicalSupportCase supportCase, PanelIdentityCaseState state,
        CancellationToken ct, PanelLabelIdentityObservation? observation = null)
    {
        if (state.Conflicts is { Count: > 0 }) return Snapshot(state, BuildConflictReply(state.Conflicts), true, observation);
        if (state.PixelPitch is null || state.PanelCode is null || state.SecondaryCode is null)
        {
            var missing = state.PixelPitch is null ? "Pitch" : state.PanelCode is null ? "Panel kodu" : "Son kod";
            return Snapshot(state, $"Panel kimliği için {missing} bilgisini de paylaşır mısınız?", true, observation);
        }
        if (state.ExactCustomerPatternMessageId is null)
        {
            var suggestion = $"{state.PixelPitch}+{state.PanelCode}+{state.SecondaryCode}";
            return Snapshot(state with { CanonicalKey = null, PackageId = null, PackageResolution = "NotAttempted", ResolutionState = PanelIdentityResolutionState.Candidate },
                $"Dosya paketini doğrulamak için lütfen {suggestion} kodunu tek satırda, aynen gönderin.", true, observation);
        }
        var normalized = normalizer.Normalize(state.PixelPitch, state.PanelCode, state.SecondaryCode);
        if (normalized.Status != PanelIdentityNormalizationStatus.Valid || normalized.Identity?.CanonicalKey is null)
            return Snapshot(state, "Panel kodu geçerli görünmüyor. P2.5+1065S+2012 formatında tekrar paylaşır mısınız?", true, observation);
        var identity = normalized.Identity;
        var resolution = await resolver.ResolveAsync(identity, ct);
        var final = state with { PixelPitch = identity.PixelPitch, PanelCode = identity.PanelCode, SecondaryCode = identity.SecondaryCode,
            CanonicalKey = identity.CanonicalKey, PackageResolution = resolution.Status.ToString(),
            PackageId = resolution.Status == PanelFilePackageResolutionStatus.ExactMatch ? resolution.PackageId : null,
            ResolutionState = Map(resolution.Status), UpdatedAt = DateTimeOffset.UtcNow };
        await PersistAsync(supportCase, final, ct);
        var display = $"{identity.PixelPitch} + {identity.PanelCode} + {identity.SecondaryCode}";
        var photoReply = IsPhoto(state.Source);
        var reply = resolution.Status switch
        {
            PanelFilePackageResolutionStatus.ExactMatch => photoReply ? $"Etiketi {display} olarak okudum." : $"Tamam, {display} olarak aldım.",
            PanelFilePackageResolutionStatus.NoMatch => $"Tamam, {display} olarak aldım. Bu kod için kayıtlı doğrulanmış bir dosya paketi bulamadım.",
            PanelFilePackageResolutionStatus.IncompletePackage => $"Tamam, {display} olarak aldım. Eşleşen paket henüz eksik dosya nedeniyle hazır değil.",
            _ => $"Tamam, {display} olarak aldım. Eşleşen paket şu anda teslimata uygun değil."
        };
        return Snapshot(final, reply, true, observation, true);
    }

    private async Task PersistAsync(TechnicalSupportCase supportCase, PanelIdentityCaseState state, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await db.TechnicalSupportCases.Where(x => x.Id == supportCase.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.PanelIdentityStateJson, json), ct);
        supportCase.PanelIdentityStateJson = json;
    }

    private static PanelIdentityTurnResult Snapshot(PanelIdentityCaseState? state, string reply, bool attempted,
        PanelLabelIdentityObservation? observation = null, bool packageAttempted = false) => new(attempted, reply,
        JsonSerializer.Serialize(new { panelIdentityAttempted = attempted, panelIdentityResolutionStatus = state?.ResolutionState,
            state?.PixelPitch, pixelPitchSource = state?.PixelPitchSource ?? state?.Source, state?.PanelCode,
            panelCodeSource = state?.PanelCodeSource ?? state?.Source, state?.SecondaryCode,
            secondaryCodeSource = state?.SecondaryCodeSource ?? state?.Source,
            conflictDetected = state?.Conflicts is { Count: > 0 }, conflictFields = state?.Conflicts?.Select(x => x.Field).Distinct(),
            canonicalPanelKey = state?.CanonicalKey, packageResolutionAttempted = packageAttempted,
            packageResolutionResult = state?.PackageResolution, exactPackageId = state?.PackageId,
            observation?.RecognitionStatus, observation?.OcrUsed, observation?.VisionUsed }, JsonOptions));

    private static void AddConflict(List<PanelIdentityConflict> conflicts, PanelIdentityField field, string? current,
        PanelIdentitySource? currentSource, string? candidate, PanelIdentitySource candidateSource, Guid messageId)
    {
        if (current is null || candidate is null || string.Equals(current, candidate, StringComparison.OrdinalIgnoreCase)) return;
        if (!conflicts.Any(x => x.Field == field && string.Equals(x.CandidateValue, candidate, StringComparison.OrdinalIgnoreCase)))
            conflicts.Add(new(field, current, currentSource ?? PanelIdentitySource.ExistingCurrentCaseFact, candidate, candidateSource, messageId));
    }

    private static string BuildConflictReply(IReadOnlyList<PanelIdentityConflict>? conflicts)
    {
        var fields = string.Join(" ve ", (conflicts ?? []).Select(x => x.Field switch { PanelIdentityField.PixelPitch => "pitch", PanelIdentityField.PanelCode => "panel kodu", _ => "son kod" }).Distinct());
        var detail = (conflicts ?? []).Count == 1 ? $" ({conflicts![0].CurrentValue} / {conflicts[0].CandidateValue})" : "";
        return $"Panel bilgilerinde {fields} çelişiyor{detail}. Doğru değeri kontrol edip yazar mısınız?";
    }

    private static PanelIdentityResolutionState Map(PanelFilePackageResolutionStatus status) => status switch
    {
        PanelFilePackageResolutionStatus.ExactMatch => PanelIdentityResolutionState.ExactPackageMatch,
        PanelFilePackageResolutionStatus.NoMatch => PanelIdentityResolutionState.PackageNoMatch,
        PanelFilePackageResolutionStatus.IncompletePackage => PanelIdentityResolutionState.PackageIncomplete,
        PanelFilePackageResolutionStatus.InactivePackage => PanelIdentityResolutionState.PackageInactive,
        PanelFilePackageResolutionStatus.Ambiguous => PanelIdentityResolutionState.PackageAmbiguous,
        _ => PanelIdentityResolutionState.CompleteValidated,
    };
    private static string? GetField(PanelIdentity? identity, PanelIdentityField field) => field switch { PanelIdentityField.PixelPitch => identity?.PixelPitch, PanelIdentityField.PanelCode => identity?.PanelCode, _ => identity?.SecondaryCode };
    private static bool ValuesEqual(PanelIdentityCaseState state, string? pitch, string? panel, string? secondary) =>
        string.Equals(state.PixelPitch, pitch, StringComparison.OrdinalIgnoreCase) && string.Equals(state.PanelCode, panel, StringComparison.OrdinalIgnoreCase) && string.Equals(state.SecondaryCode, secondary, StringComparison.OrdinalIgnoreCase);
    private static int DistinctSources(params PanelIdentitySource?[] values) => values.Where(x => x is not null).Distinct().Count();
    private static bool IsPhoto(PanelIdentitySource? source) => source is PanelIdentitySource.PanelLabelOcr or PanelIdentitySource.PanelLabelVision or PanelIdentitySource.OcrAndVision;
    private static bool IsText(PanelIdentitySource? source) => source is PanelIdentitySource.CustomerPattern or PanelIdentitySource.CustomerNaturalText or PanelIdentitySource.CustomerCorrection or PanelIdentitySource.OperatorCorrection;
    private static string? NormalizePitch(string? value) => value?.Trim().ToUpperInvariant().Replace(',', '.');
    private static string? NormalizeToken(string? value) => value?.Trim().ToUpperInvariant();
    private static bool IsIsolatedSecondary(string? text) => !string.IsNullOrWhiteSpace(text) && IsolatedSecondaryRegex().IsMatch(text);
    private static PanelIdentityCaseState? Deserialize(string? json) { try { return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<PanelIdentityCaseState>(json, JsonOptions); } catch (JsonException) { return null; } }
    private static JsonSerializerOptions CreateOptions() { var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); options.Converters.Add(new JsonStringEnumConverter()); return options; }
    [GeneratedRegex(@"^\s*\d{2,32}\s*[.!]?\s*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)] private static partial Regex IsolatedSecondaryRegex();
    [GeneratedRegex(@"^\s*([A-Z0-9.,]{2,32})\s+(?:doğru|dogru)\s*[.!]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)] private static partial Regex CorrectCandidateRegex();
    [GeneratedRegex(@"\bnerede\s+kalm[ıi]şt[ıi]k\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)] private static partial Regex ResumeRegex();
}
