using System.Text.RegularExpressions;

namespace LedSupBot.Application.PanelFiles;

public enum PanelLabelRecognitionStatus { CompleteHighConfidence, CompleteNeedsConfirmation, Partial, Ambiguous, Unreadable, MultipleLabels, ConflictingEvidence, ProviderFailure }

public sealed record PanelLabelIdentityObservation(
    string? PixelPitchCandidate, string? PanelCodeCandidate, string? SecondaryCodeCandidate,
    PanelLabelRecognitionStatus RecognitionStatus,
    double PixelPitchConfidence, double PanelCodeConfidence, double SecondaryCodeConfidence,
    string Evidence, IReadOnlyList<string> Warnings, bool OcrUsed, bool VisionUsed);

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
        if (UncertainRegex().IsMatch(evidence)) return Partial(evidence, PanelLabelRecognitionStatus.Ambiguous, true, false, "uncertain_character");
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
        if (Conflicts(ocr.PixelPitchCandidate, vision.PixelPitchCandidate) || Conflicts(ocr.PanelCodeCandidate, vision.PanelCodeCandidate) || Conflicts(ocr.SecondaryCodeCandidate, vision.SecondaryCodeCandidate))
            return new(null, null, null, PanelLabelRecognitionStatus.ConflictingEvidence, 0, 0, 0,
                Bound($"{ocr.Evidence} | {vision.Evidence}"), ["ocr_vision_conflict"], ocr.OcrUsed, true);
        var pitch = vision.PixelPitchCandidate ?? ocr.PixelPitchCandidate;
        var panel = vision.PanelCodeCandidate ?? ocr.PanelCodeCandidate;
        var secondary = vision.SecondaryCodeCandidate ?? ocr.SecondaryCodeCandidate;
        var complete = pitch is not null && panel is not null && secondary is not null;
        return new(pitch, panel, secondary, complete ? PanelLabelRecognitionStatus.CompleteNeedsConfirmation : PanelLabelRecognitionStatus.Partial,
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

    private static PanelLabelIdentityObservation Empty(PanelLabelRecognitionStatus status, bool ocr, bool vision, string warning) => new(null, null, null, status, 0, 0, 0, "", [warning], ocr, vision);
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
