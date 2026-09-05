using System.Text.RegularExpressions;
using LedSupBot.Domain.PanelFiles;

namespace LedSupBot.Application.PanelFiles;

public enum PanelIdentityTextStatus { NoIdentity, CompleteIdentity, PartialIdentity, AmbiguousIdentity, InvalidIdentity, PhotoIdentityRetraction, TextIdentityRetraction }
public enum PanelIdentitySource { CustomerPattern, CustomerNaturalText, ExistingCurrentCaseFact, OperatorCorrection, CustomerCorrection, PanelLabelOcr, PanelLabelVision, OcrAndVision, MixedContext }
public enum PanelIdentityField { PixelPitch, PanelCode, SecondaryCode }
public enum PanelIdentityResolutionState { MissingInformation, Candidate, CompleteValidated, IdentityConflict, ExactPackageMatch, PackageNoMatch, PackageIncomplete, PackageInactive, PackageAmbiguous }

public sealed record PanelIdentityTextResult(
    PanelIdentityTextStatus Status,
    PanelIdentity? Identity = null,
    IReadOnlyList<string>? MissingFields = null,
    PanelIdentitySource? Source = null,
    string? Evidence = null,
    PanelIdentityField? CorrectedField = null);

public interface IPanelIdentityTextParser { PanelIdentityTextResult Parse(string? text); }

public sealed partial class PanelIdentityTextParser(IPanelIdentityNormalizer normalizer) : IPanelIdentityTextParser
{
    public const int MaxInputLength = 4000;

    public PanelIdentityTextResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(PanelIdentityTextStatus.NoIdentity);
        var bounded = text.Trim();
        if (bounded.Length > MaxInputLength) bounded = bounded[..MaxInputLength];
        if (WrongPhotoRegex().IsMatch(bounded)) return new(PanelIdentityTextStatus.PhotoIdentityRetraction, Evidence: Bound(bounded));
        if (WrongTextRegex().IsMatch(bounded)) return new(PanelIdentityTextStatus.TextIdentityRetraction, Evidence: Bound(bounded));

        var pitchCorrection = PitchCorrectionRegex().Match(bounded);
        if (pitchCorrection.Success)
            return new(PanelIdentityTextStatus.PartialIdentity, new(pitchCorrection.Groups[1].Value, null, null),
                [nameof(PanelIdentity.PanelCode), nameof(PanelIdentity.SecondaryCode)], PanelIdentitySource.CustomerCorrection, Bound(pitchCorrection.Value), PanelIdentityField.PixelPitch);
        var secondaryCorrection = SecondaryCorrectionRawRegex().Match(bounded);
        if (secondaryCorrection.Success)
            return new(PanelIdentityTextStatus.PartialIdentity, new(null, null,
                    (secondaryCorrection.Groups[1].Success ? secondaryCorrection.Groups[1] : secondaryCorrection.Groups[2]).Value),
                [nameof(PanelIdentity.PixelPitch), nameof(PanelIdentity.PanelCode)], PanelIdentitySource.CustomerCorrection, Bound(secondaryCorrection.Value), PanelIdentityField.SecondaryCode);
        var negativeCorrection = NegativeSecondaryCorrectionRegex().Match(bounded);
        if (negativeCorrection.Success)
            return new(PanelIdentityTextStatus.PartialIdentity, new(null, null, negativeCorrection.Groups[1].Value),
                [nameof(PanelIdentity.PixelPitch), nameof(PanelIdentity.PanelCode)], PanelIdentitySource.CustomerCorrection, Bound(negativeCorrection.Value), PanelIdentityField.SecondaryCode);
        var correction = CorrectionRegex().Match(bounded);
        if (correction.Success)
            return new(PanelIdentityTextStatus.PartialIdentity, new(null, null, correction.Groups[1].Value),
                [nameof(PanelIdentity.PixelPitch), nameof(PanelIdentity.PanelCode)], PanelIdentitySource.OperatorCorrection, Bound(correction.Value), PanelIdentityField.SecondaryCode);

        var completeMatches = CompleteRegex().Matches(bounded);
        if (completeMatches.Count > 1 && completeMatches.Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            return new(PanelIdentityTextStatus.AmbiguousIdentity, Evidence: Bound(bounded));
        var complete = completeMatches.Count > 0 ? completeMatches[0] : Match.Empty;
        if (complete.Success)
        {
            var normalized = normalizer.Normalize(complete.Groups[1].Value, complete.Groups[2].Value, complete.Groups[3].Value);
            if (normalized.Status != PanelIdentityNormalizationStatus.Valid) return new(PanelIdentityTextStatus.InvalidIdentity, Evidence: Bound(complete.Value));
            var source = string.Equals(complete.Value.Trim(), bounded, StringComparison.Ordinal) ? PanelIdentitySource.CustomerPattern : PanelIdentitySource.CustomerNaturalText;
            return new(PanelIdentityTextStatus.CompleteIdentity, normalized.Identity, [], source, Bound(complete.Value));
        }
        var missingPitch = MissingPitchRegex().Match(bounded);
        if (missingPitch.Success)
            return new(PanelIdentityTextStatus.PartialIdentity, new(null, missingPitch.Groups[1].Value.ToUpperInvariant(), missingPitch.Groups[2].Value.ToUpperInvariant()),
                [nameof(PanelIdentity.PixelPitch)], PanelIdentitySource.CustomerPattern, Bound(missingPitch.Value));
        var missingSecondary = MissingSecondaryRegex().Match(bounded);
        if (missingSecondary.Success)
            return new(PanelIdentityTextStatus.PartialIdentity, new(missingSecondary.Groups[1].Value, missingSecondary.Groups[2].Value, null),
                [nameof(PanelIdentity.SecondaryCode)], PanelIdentitySource.CustomerPattern, Bound(missingSecondary.Value));
        if (AmbiguousRegex().IsMatch(bounded)) return new(PanelIdentityTextStatus.AmbiguousIdentity, Evidence: Bound(bounded));
        return new(PanelIdentityTextStatus.NoIdentity);
    }

    private static string Bound(string value) => value.Length <= 160 ? value : value[..160];
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
    [GeneratedRegex(@"\b(P\d+(?:[.,]\d{1,3})?)\s*(?:\+|-|_|/|,|\s)\s*([A-Z0-9]{2,32})\s*(?:\+|-|_|/|,|\s)\s*([A-Z0-9]{2,32})\b", Options)] private static partial Regex CompleteRegex();
    [GeneratedRegex(@"^\s*((?:\d{1,31}[A-Z][A-Z0-9]*|[A-Z][A-Z0-9]{0,30}\d[A-Z0-9]*))\s*(?:\+|/|,)\s*([A-Z0-9]{2,32})\s*[.!?]?\s*$", Options)] private static partial Regex MissingPitchRegex();
    [GeneratedRegex(@"^\s*(P\d+(?:[.,]\d{1,3})?)\s*(?:\+|/|,)\s*((?:\d{1,31}[A-Z][A-Z0-9]*|[A-Z][A-Z0-9]{0,30}\d[A-Z0-9]*))\s*[.!?]?\s*$", Options)] private static partial Regex MissingSecondaryRegex();
    [GeneratedRegex(@"^\s*P\d+(?:[.,]\d{1,3})?\s*(?:\+|/|,)\s*\d{2,32}\s*[.!?]?\s*$", Options)] private static partial Regex AmbiguousRegex();
    [GeneratedRegex(@"\b(?:hay[ıi]r\s+)?(?:son\s+kod|secondary\s*(?:code|kod))\s*(?:=|:)?\s*([A-Z0-9]{2,32})\b", Options)] private static partial Regex CorrectionRegex();
    [GeneratedRegex(@"\b(?:\d{2,32}\s+(?:değil|degil)\s+)?(\d{2,32})\s+(?:doğru|dogru)\b|\b\d{2,32}\s+(?:değil|degil)\s+(\d{2,32})\b", Options)] private static partial Regex SecondaryCorrectionRawRegex();
    [GeneratedRegex(@"\bP\d+(?:[.,]\d{1,3})?\s+(?:değil|degil)\s+(P\d+(?:[.,]\d{1,3})?)\b", Options)] private static partial Regex PitchCorrectionRegex();
    [GeneratedRegex(@"^\s*(?:hayır|hayir)\s*[,]?\s*(\d{2,32})\s+(?:olacak|doğru|dogru)\s*[.!]?\s*$", Options)] private static partial Regex NegativeSecondaryCorrectionRegex();
    [GeneratedRegex(@"\b(?:yanl[ıi]ş|hatali|hatalı)\s+panel(?:in)?\s+(?:foto(?:ğraf|graf)(?:ını|ini)?|resm(?:ini|i))", Options)] private static partial Regex WrongPhotoRegex();
    [GeneratedRegex(@"\b(?:az\s+önce\s+)?yazdığım\s+kod\s+(?:yanlış(?:tı)?|hatalı(?:ydı)?)", Options)] private static partial Regex WrongTextRegex();
}

public sealed record PanelIdentityConflict(PanelIdentityField Field, string CurrentValue, PanelIdentitySource CurrentSource,
    string CandidateValue, PanelIdentitySource CandidateSource, Guid? CandidateMessageId);

public sealed record PanelIdentityCaseState(
    string? PixelPitch, string? PanelCode, string? SecondaryCode, string? CanonicalKey,
    PanelIdentitySource Source, string PackageResolution, Guid? PackageId, DateTimeOffset UpdatedAt,
    PanelIdentitySource? PixelPitchSource = null, PanelIdentitySource? PanelCodeSource = null, PanelIdentitySource? SecondaryCodeSource = null,
    Guid? SourceMessageId = null, PanelIdentityResolutionState ResolutionState = PanelIdentityResolutionState.MissingInformation,
    IReadOnlyList<PanelIdentityConflict>? Conflicts = null, Guid? PixelPitchMessageId = null, Guid? PanelCodeMessageId = null,
    Guid? SecondaryCodeMessageId = null);

public sealed record PanelIdentityTurnResult(bool Attempted, string? Reply = null, string ContextJson = "{}");
