using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Media;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.PanelFiles;
using Microsoft.EntityFrameworkCore;

namespace LedSupBot.Application.PanelFiles;

public enum PanelLabelRecognitionStatus
{
    CompleteHighConfidence,
    CompleteNeedsConfirmation,
    Partial,
    Ambiguous,
    Unreadable,
    MultipleLabels,
    ConflictingEvidence,
    ProviderFailure
}

public sealed record PanelLabelIdentityObservation(
    string? PixelPitchCandidate,
    string? PanelCodeCandidate,
    string? SecondaryCodeCandidate,
    PanelLabelRecognitionStatus RecognitionStatus,
    double PixelPitchConfidence,
    double PanelCodeConfidence,
    double SecondaryCodeConfidence,
    string Evidence,
    IReadOnlyList<string> Warnings,
    bool OcrUsed,
    bool VisionUsed);

public static partial class PanelLabelIdentityRecognizer
{
    public static PanelLabelIdentityObservation FromOcr(string? text)
    {
        var evidence = Bound(text);
        if (string.IsNullOrWhiteSpace(evidence)) return Empty(PanelLabelRecognitionStatus.Unreadable, false, false, "ocr_unreadable");
        var complete = CompleteRegex().Matches(evidence);
        if (complete.Count > 1)
        {
            var keys = complete.Select(m => string.Join('-', m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (keys.Count > 1) return Empty(PanelLabelRecognitionStatus.MultipleLabels, true, false, "multiple_labels");
        }
        if (UncertainRegex().IsMatch(evidence))
            return Partial(evidence, PanelLabelRecognitionStatus.Ambiguous, true, false, "uncertain_character");
        if (complete.Count == 1)
        {
            var m = complete[0];
            return new(NormalizePitch(m.Groups[1].Value), m.Groups[2].Value.ToUpperInvariant(), m.Groups[3].Value.ToUpperInvariant(),
                PanelLabelRecognitionStatus.CompleteHighConfidence, .95, .95, .95, Bound(m.Value), [], true, false);
        }
        return Partial(evidence, PanelLabelRecognitionStatus.Partial, true, false, "missing_identity_field");
    }

    public static PanelLabelIdentityObservation Merge(PanelLabelIdentityObservation ocr, PanelLabelIdentityObservation vision)
    {
        if (ocr.RecognitionStatus == PanelLabelRecognitionStatus.CompleteHighConfidence) return ocr;
        if (vision.RecognitionStatus is PanelLabelRecognitionStatus.MultipleLabels or PanelLabelRecognitionStatus.Ambiguous)
            return vision with { OcrUsed = ocr.OcrUsed, VisionUsed = true };
        if (Conflicts(ocr.PixelPitchCandidate, vision.PixelPitchCandidate) ||
            Conflicts(ocr.PanelCodeCandidate, vision.PanelCodeCandidate) ||
            Conflicts(ocr.SecondaryCodeCandidate, vision.SecondaryCodeCandidate))
            return new(null, null, null, PanelLabelRecognitionStatus.ConflictingEvidence, 0, 0, 0,
                Bound($"{ocr.Evidence} | {vision.Evidence}"), ["ocr_vision_conflict"], ocr.OcrUsed, true);
        var pitch = vision.PixelPitchCandidate ?? ocr.PixelPitchCandidate;
        var panel = vision.PanelCodeCandidate ?? ocr.PanelCodeCandidate;
        var secondary = vision.SecondaryCodeCandidate ?? ocr.SecondaryCodeCandidate;
        var complete = pitch is not null && panel is not null && secondary is not null;
        return new(pitch, panel, secondary,
            complete ? PanelLabelRecognitionStatus.CompleteNeedsConfirmation : PanelLabelRecognitionStatus.Partial,
            vision.PixelPitchCandidate is null ? ocr.PixelPitchConfidence : vision.PixelPitchConfidence,
            vision.PanelCodeCandidate is null ? ocr.PanelCodeConfidence : vision.PanelCodeConfidence,
            vision.SecondaryCodeCandidate is null ? ocr.SecondaryCodeConfidence : vision.SecondaryCodeConfidence,
            Bound($"{ocr.Evidence} | {vision.Evidence}"), ocr.Warnings.Concat(vision.Warnings).Distinct().Take(8).ToList(), ocr.OcrUsed, true);
    }

    private static PanelLabelIdentityObservation Partial(string text, PanelLabelRecognitionStatus status, bool ocr, bool vision, string warning)
    {
        var pitch = PitchRegex().Match(text) is { Success: true } p ? NormalizePitch(p.Value) : null;
        var panel = PanelRegex().Match(text) is { Success: true } c ? c.Value.ToUpperInvariant() : null;
        var secondaryMatches = SecondaryRegex().Matches(text).Select(x => x.Value).Where(x => !string.Equals(x, panel, StringComparison.OrdinalIgnoreCase)).ToList();
        var secondary = secondaryMatches.Count == 1 ? secondaryMatches[0].ToUpperInvariant() : null;
        return new(pitch, panel, secondary, status, pitch is null ? 0 : .9, panel is null ? 0 : .9, secondary is null ? 0 : .9,
            Bound(text), [warning], ocr, vision);
    }

    private static PanelLabelIdentityObservation Empty(PanelLabelRecognitionStatus status, bool ocr, bool vision, string warning) =>
        new(null, null, null, status, 0, 0, 0, "", [warning], ocr, vision);
    private static bool Conflicts(string? a, string? b) => a is not null && b is not null && !string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static string NormalizePitch(string value) => value.ToUpperInvariant().Replace(',', '.');
    private static string Bound(string? value) => string.IsNullOrWhiteSpace(value) ? "" : value.Trim()[..Math.Min(value.Trim().Length, 500)];

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
    [GeneratedRegex(@"\b(P\d+(?:[.,]\d{1,3})?)\s*(?:\+|-|_|/|,|\s)+\s*([A-Z0-9]{2,32})\s*(?:\+|-|_|/|,|\s)+\s*([A-Z0-9]{2,32})\b", Options)] private static partial Regex CompleteRegex();
    [GeneratedRegex(@"\bP\d+(?:[.,]\d{1,3})?\b", Options)] private static partial Regex PitchRegex();
    [GeneratedRegex(@"\b(?:\d{1,31}[A-Z][A-Z0-9]*|[A-Z][A-Z0-9]{0,30}\d[A-Z0-9]*)\b", Options)] private static partial Regex PanelRegex();
    [GeneratedRegex(@"\b\d{4,32}\b", Options)] private static partial Regex SecondaryRegex();
    [GeneratedRegex(@"\?", Options)] private static partial Regex UncertainRegex();
}

public sealed class PanelIdentityPhotoWorkflowService(
    IApplicationDbContext db,
    IPanelIdentityNormalizer normalizer,
    IPanelFilePackageResolver resolver)
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public async Task<PanelIdentityTurnResult> ApplyAsync(TechnicalSupportCase supportCase, ValidatedImageAnalysis? image, Guid sourceMessageId, CancellationToken ct)
    {
        var observation = image?.Analysis.PanelIdentityObservation;
        if (observation is null) return new(false);
        var prior = Deserialize(supportCase.PanelIdentityStateJson);
        if (observation.RecognitionStatus is PanelLabelRecognitionStatus.Unreadable or PanelLabelRecognitionStatus.ProviderFailure)
            return Result(observation, null, false, "NotAttempted", null, "Etiketteki kodlar net görünmüyor. Biraz daha yakından ve ışık yansımadan tekrar çekebilir misiniz?");
        if (observation.RecognitionStatus == PanelLabelRecognitionStatus.MultipleLabels)
            return Result(observation, null, false, "NotAttempted", null, "Fotoğrafta birden fazla etiket görünüyor. İlgili tek etiketi yakından gönderir misiniz?");
        if (observation.RecognitionStatus is PanelLabelRecognitionStatus.Ambiguous or PanelLabelRecognitionStatus.ConflictingEvidence)
            return Result(observation, null, false, "NotAttempted", null, "Son kod net görünmüyor. Etiketi biraz daha yakından tekrar gönderebilir misiniz?");
        if (observation.PixelPitchCandidate is not null && observation.PanelCodeCandidate is not null && observation.SecondaryCodeCandidate is not null &&
            Math.Min(observation.PixelPitchConfidence, Math.Min(observation.PanelCodeConfidence, observation.SecondaryCodeConfidence)) < .78)
            return Result(observation, null, false, "NotAttempted", null, "Etiketteki kodlardan biri yeterince net görünmüyor. Etiketi biraz daha yakından tekrar gönderebilir misiniz?");

        var pitch = observation.PixelPitchCandidate ?? prior?.PixelPitch;
        var panel = observation.PanelCodeCandidate;
        var secondary = observation.SecondaryCodeCandidate;
        if (prior?.CanonicalKey is not null &&
            (Conflicts(prior.PixelPitch, observation.PixelPitchCandidate) || Conflicts(prior.PanelCode, panel) || Conflicts(prior.SecondaryCode, secondary)))
            return Result(observation, null, false, "NotAttempted", null,
                $"Yazdığınız bilgide son kod {prior.SecondaryCode}, fotoğrafta ise {secondary ?? "farklı"} görünüyor. Son kodu kontrol eder misiniz?", true);

        if (pitch is null || panel is null || secondary is null)
        {
            var partial = new PanelIdentityCaseState(pitch, panel, secondary, null,
                observation.OcrUsed && observation.VisionUsed ? PanelIdentitySource.OcrAndVision : observation.VisionUsed ? PanelIdentitySource.PanelLabelVision : PanelIdentitySource.PanelLabelOcr,
                "NotAttempted", null, DateTimeOffset.UtcNow,
                pitch is not null && observation.PixelPitchCandidate is null ? prior?.PixelPitchSource ?? prior?.Source : observation.VisionUsed ? PanelIdentitySource.PanelLabelVision : PanelIdentitySource.PanelLabelOcr,
                panel is null ? null : observation.VisionUsed ? PanelIdentitySource.PanelLabelVision : PanelIdentitySource.PanelLabelOcr,
                secondary is null ? null : observation.VisionUsed ? PanelIdentitySource.PanelLabelVision : PanelIdentitySource.PanelLabelOcr, sourceMessageId);
            await PersistAsync(supportCase, partial, ct);
            return Result(observation, null, false, "NotAttempted", null, pitch is null
                ? "Etikette pitch bilgisi görünmüyor. P2.5 gibi pitch değerini yazabilir misiniz?"
                : "Etiketteki panel kodları tamamlanamadı. Etiketi daha yakından tekrar gönderebilir misiniz?");
        }

        var normalized = normalizer.Normalize(pitch, panel, secondary);
        if (normalized.Status != PanelIdentityNormalizationStatus.Valid || normalized.Identity?.CanonicalKey is null)
            return Result(observation, null, false, "NotAttempted", null, "Etiketteki panel kodu geçerli görünmüyor. Daha net bir fotoğraf gönderebilir misiniz?");
        var identity = normalized.Identity;
        var resolution = await resolver.ResolveAsync(identity, ct);
        var photoSource = observation.OcrUsed && observation.VisionUsed ? PanelIdentitySource.OcrAndVision : observation.VisionUsed ? PanelIdentitySource.PanelLabelVision : PanelIdentitySource.PanelLabelOcr;
        var mixed = observation.PixelPitchCandidate is null && prior?.PixelPitch is not null;
        var state = new PanelIdentityCaseState(identity.PixelPitch, identity.PanelCode, identity.SecondaryCode, identity.CanonicalKey,
            mixed ? PanelIdentitySource.MixedContext : photoSource, resolution.Status.ToString(),
            resolution.Status == PanelFilePackageResolutionStatus.ExactMatch ? resolution.PackageId : null, DateTimeOffset.UtcNow,
            mixed ? prior?.PixelPitchSource ?? prior?.Source : photoSource, photoSource, photoSource, sourceMessageId);
        await PersistAsync(supportCase, state, ct);
        var display = $"{identity.PixelPitch} + {identity.PanelCode} + {identity.SecondaryCode}";
        var reply = resolution.Status switch
        {
            PanelFilePackageResolutionStatus.ExactMatch => $"Etiketi {display} olarak okudum.",
            PanelFilePackageResolutionStatus.NoMatch => $"Etiketi {display} olarak okudum ama bu kod için kayıtlı doğrulanmış bir dosya paketi bulamadım.",
            PanelFilePackageResolutionStatus.IncompletePackage => $"Etiketi {display} olarak okudum. Eşleşen paket henüz eksik dosya nedeniyle hazır değil.",
            _ => $"Etiketi {display} olarak okudum. Eşleşen paket şu anda teslimata uygun değil."
        };
        return Result(observation, identity, true, resolution.Status.ToString(), state.PackageId, reply, false, state);
    }

    private async Task PersistAsync(TechnicalSupportCase supportCase, PanelIdentityCaseState state, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await db.TechnicalSupportCases.Where(x => x.Id == supportCase.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.PanelIdentityStateJson, json), ct);
        supportCase.PanelIdentityStateJson = json;
    }

    private static PanelIdentityTurnResult Result(PanelLabelIdentityObservation observation, PanelIdentity? identity, bool resolutionAttempted,
        string resolution, Guid? packageId, string reply, bool conflict = false, PanelIdentityCaseState? state = null) =>
        new(true, reply, JsonSerializer.Serialize(new { panelIdentityAttempted = true,
            panelIdentitySource = state?.Source ?? (observation.OcrUsed && observation.VisionUsed ? PanelIdentitySource.OcrAndVision : observation.VisionUsed ? PanelIdentitySource.PanelLabelVision : PanelIdentitySource.PanelLabelOcr),
            observation.RecognitionStatus, observation.OcrUsed, observation.VisionUsed,
            detectedPixelPitch = observation.PixelPitchCandidate, detectedPanelCode = observation.PanelCodeCandidate,
            detectedSecondaryCode = observation.SecondaryCodeCandidate, observation.PixelPitchConfidence, observation.PanelCodeConfidence,
            observation.SecondaryCodeConfidence, identityConflictDetected = conflict, canonicalPanelKey = identity?.CanonicalKey,
            packageResolutionAttempted = resolutionAttempted, packageResolutionResult = resolution, packageId,
            pixelPitchSource = state?.PixelPitchSource, panelCodeSource = state?.PanelCodeSource, secondaryCodeSource = state?.SecondaryCodeSource }, JsonOptions));

    private static bool Conflicts(string? a, string? b) => a is not null && b is not null && !string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static PanelIdentityCaseState? Deserialize(string? json) { try { return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<PanelIdentityCaseState>(json, JsonOptions); } catch (JsonException) { return null; } }
    private static JsonSerializerOptions CreateOptions() { var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); options.Converters.Add(new JsonStringEnumConverter()); return options; }
}
