using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using LedSupBot.Application.Abstractions;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.PanelFiles;
using Microsoft.EntityFrameworkCore;

namespace LedSupBot.Application.PanelFiles;

public enum PanelIdentityTextStatus { NoIdentity, CompleteIdentity, PartialIdentity, AmbiguousIdentity, InvalidIdentity, PhotoIdentityRetraction }
public enum PanelIdentitySource { CustomerPattern, CustomerNaturalText, ExistingCurrentCaseFact, OperatorCorrection, PanelLabelOcr, PanelLabelVision, OcrAndVision, MixedContext }

public sealed record PanelIdentityTextResult(
    PanelIdentityTextStatus Status,
    PanelIdentity? Identity = null,
    IReadOnlyList<string>? MissingFields = null,
    PanelIdentitySource? Source = null,
    string? Evidence = null);

public interface IPanelIdentityTextParser
{
    PanelIdentityTextResult Parse(string? text);
}

public sealed partial class PanelIdentityTextParser(IPanelIdentityNormalizer normalizer) : IPanelIdentityTextParser
{
    public const int MaxInputLength = 4000;

    public PanelIdentityTextResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(PanelIdentityTextStatus.NoIdentity);
        var bounded = text.Trim();
        if (bounded.Length > MaxInputLength) bounded = bounded[..MaxInputLength];

        if (WrongPhotoRegex().IsMatch(bounded))
            return new(PanelIdentityTextStatus.PhotoIdentityRetraction, Evidence: BoundedEvidence(bounded));

        var correction = CorrectionRegex().Match(bounded);
        if (correction.Success)
            return new(PanelIdentityTextStatus.PartialIdentity,
                new PanelIdentity(null, null, correction.Groups[1].Value.ToUpperInvariant()),
                [nameof(PanelIdentity.PixelPitch), nameof(PanelIdentity.PanelCode)], PanelIdentitySource.OperatorCorrection,
                BoundedEvidence(correction.Value));

        var complete = CompleteRegex().Match(bounded);
        if (complete.Success)
        {
            var normalized = normalizer.Normalize(complete.Groups[1].Value, complete.Groups[2].Value, complete.Groups[3].Value);
            if (normalized.Status != PanelIdentityNormalizationStatus.Valid)
                return new(PanelIdentityTextStatus.InvalidIdentity, Evidence: BoundedEvidence(complete.Value));
            var source = string.Equals(complete.Value.Trim(), bounded, StringComparison.Ordinal)
                ? PanelIdentitySource.CustomerPattern : PanelIdentitySource.CustomerNaturalText;
            return new(PanelIdentityTextStatus.CompleteIdentity, normalized.Identity, [], source, BoundedEvidence(complete.Value));
        }

        var missingPitch = MissingPitchRegex().Match(bounded);
        if (missingPitch.Success)
            return new(PanelIdentityTextStatus.PartialIdentity,
                new PanelIdentity(null, missingPitch.Groups[1].Value.ToUpperInvariant(), missingPitch.Groups[2].Value.ToUpperInvariant()),
                [nameof(PanelIdentity.PixelPitch)], PanelIdentitySource.CustomerPattern, BoundedEvidence(missingPitch.Value));

        var missingSecondary = MissingSecondaryRegex().Match(bounded);
        if (missingSecondary.Success)
            return new(PanelIdentityTextStatus.PartialIdentity,
                new PanelIdentity(missingSecondary.Groups[1].Value.ToUpperInvariant().Replace(',', '.'), missingSecondary.Groups[2].Value.ToUpperInvariant(), null),
                [nameof(PanelIdentity.SecondaryCode)], PanelIdentitySource.CustomerPattern, BoundedEvidence(missingSecondary.Value));

        if (AmbiguousRegex().IsMatch(bounded))
            return new(PanelIdentityTextStatus.AmbiguousIdentity, Evidence: BoundedEvidence(bounded));

        return new(PanelIdentityTextStatus.NoIdentity);
    }

    private static string BoundedEvidence(string value) => value.Length <= 160 ? value : value[..160];

    private const RegexOptions SafeOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
    [GeneratedRegex(@"\b(P\d+(?:[.,]\d{1,3})?)\s*(?:\+|-|_|/|,|\s)\s*([A-Z0-9]{2,32})\s*(?:\+|-|_|/|,|\s)\s*([A-Z0-9]{2,32})\b", SafeOptions)]
    private static partial Regex CompleteRegex();
    [GeneratedRegex(@"^\s*((?:\d{1,31}[A-Z][A-Z0-9]*|[A-Z][A-Z0-9]{0,30}\d[A-Z0-9]*))\s*(?:\+|/|,)\s*([A-Z0-9]{2,32})\s*[.!?]?\s*$", SafeOptions)]
    private static partial Regex MissingPitchRegex();
    [GeneratedRegex(@"^\s*(P\d+(?:[.,]\d{1,3})?)\s*(?:\+|/|,)\s*((?:\d{1,31}[A-Z][A-Z0-9]*|[A-Z][A-Z0-9]{0,30}\d[A-Z0-9]*))\s*[.!?]?\s*$", SafeOptions)]
    private static partial Regex MissingSecondaryRegex();
    [GeneratedRegex(@"^\s*P\d+(?:[.,]\d{1,3})?\s*(?:\+|/|,)\s*\d{2,32}\s*[.!?]?\s*$", SafeOptions)]
    private static partial Regex AmbiguousRegex();
    [GeneratedRegex(@"\b(?:hay[ıi]r\s+)?(?:son\s+kod|secondary\s*(?:code|kod))\s*(?:=|:)?\s*([A-Z0-9]{2,32})\b", SafeOptions)]
    private static partial Regex CorrectionRegex();
    [GeneratedRegex(@"\b(?:yanl[ıi]ş|hatali|hatalı)\s+panel(?:in)?\s+(?:foto(?:ğraf|graf)(?:ını|ini)?|resm(?:ini|i))", SafeOptions)]
    private static partial Regex WrongPhotoRegex();
}

public sealed record PanelIdentityCaseState(
    string? PixelPitch,
    string? PanelCode,
    string? SecondaryCode,
    string? CanonicalKey,
    PanelIdentitySource Source,
    string PackageResolution,
    Guid? PackageId,
    DateTimeOffset UpdatedAt,
    PanelIdentitySource? PixelPitchSource = null,
    PanelIdentitySource? PanelCodeSource = null,
    PanelIdentitySource? SecondaryCodeSource = null,
    Guid? SourceMessageId = null);

public sealed record PanelIdentityTurnResult(bool Attempted, string? Reply = null, string ContextJson = "{}");

public sealed class PanelIdentityWorkflowService(
    IApplicationDbContext db,
    IPanelIdentityTextParser parser,
    IPanelIdentityNormalizer normalizer,
    IPanelFilePackageResolver resolver)
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async Task<PanelIdentityTurnResult> ApplyAsync(TechnicalSupportCase supportCase, string? text, CancellationToken ct = default)
    {
        var parsed = parser.Parse(text);
        if (parsed.Status == PanelIdentityTextStatus.NoIdentity) return new(false);
        var previous = Deserialize(supportCase.PanelIdentityStateJson);
        if (parsed.Status == PanelIdentityTextStatus.PhotoIdentityRetraction)
        {
            if (previous?.Source is not (PanelIdentitySource.PanelLabelOcr or PanelIdentitySource.PanelLabelVision or PanelIdentitySource.OcrAndVision or PanelIdentitySource.MixedContext))
                return new(false);
            await db.TechnicalSupportCases.Where(x => x.Id == supportCase.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.PanelIdentityStateJson, (string?)null), ct);
            supportCase.PanelIdentityStateJson = null;
            return new(true, "Tamam, yanlış panele ait fotoğraftan okunan bilgileri geçersiz kıldım. Doğru panelin etiketini gönderebilir misiniz?",
                JsonSerializer.Serialize(new { panelIdentityAttempted = true, panelIdentityRetracted = true, packageResolutionAttempted = false }, JsonOptions));
        }
        var candidate = parsed.Identity;
        var source = parsed.Source ?? PanelIdentitySource.CustomerPattern;

        if (source == PanelIdentitySource.OperatorCorrection)
        {
            if (previous?.CanonicalKey is null)
                return Build(parsed, null, "NotAttempted", null, "Önce tam panel kodunu P2.5+1065S+2012 formatında paylaşır mısınız?");
            candidate = new PanelIdentity(previous.PixelPitch, previous.PanelCode, candidate?.SecondaryCode);
        }
        else if (parsed.Status == PanelIdentityTextStatus.PartialIdentity && candidate?.PixelPitch is null && previous?.PixelPitch is not null)
        {
            candidate = candidate! with { PixelPitch = previous.PixelPitch };
            source = PanelIdentitySource.ExistingCurrentCaseFact;
        }

        if (candidate?.IsComplete == true)
        {
            var normalized = normalizer.Normalize(candidate.PixelPitch, candidate.PanelCode, candidate.SecondaryCode);
            if (normalized.Status != PanelIdentityNormalizationStatus.Valid || normalized.Identity?.CanonicalKey is null)
                return Build(parsed, candidate, "NotAttempted", null, "Panel kodu geçerli görünmüyor. P2.5+1065S+2012 formatında tekrar yazar mısınız?");
            candidate = normalized.Identity;
            var resolution = await resolver.ResolveAsync(candidate, ct);
            var state = new PanelIdentityCaseState(candidate.PixelPitch, candidate.PanelCode, candidate.SecondaryCode,
                candidate.CanonicalKey, source, resolution.Status.ToString(),
                resolution.Status == PanelFilePackageResolutionStatus.ExactMatch ? resolution.PackageId : null, DateTimeOffset.UtcNow);
            await PersistAsync(supportCase, state, ct);
            var display = $"{candidate.PixelPitch} + {candidate.PanelCode} + {candidate.SecondaryCode}";
            var reply = resolution.Status switch
            {
                PanelFilePackageResolutionStatus.ExactMatch => $"Tamam, {display} olarak aldım.",
                PanelFilePackageResolutionStatus.NoMatch => $"Tamam, {display} olarak aldım. Bu kod için kayıtlı doğrulanmış bir dosya paketi bulamadım.",
                PanelFilePackageResolutionStatus.IncompletePackage => $"Tamam, {display} olarak aldım. Eşleşen paket henüz eksik dosya nedeniyle hazır değil.",
                _ => $"Tamam, {display} olarak aldım. Eşleşen paket şu anda teslimata uygun değil."
            };
            return new(true, reply, JsonSerializer.Serialize(new { panelIdentityAttempted = true, panelIdentitySource = source, panelIdentityComplete = true,
                candidate.PixelPitch, candidate.PanelCode, candidate.SecondaryCode, canonicalPanelKey = candidate.CanonicalKey,
                packageResolutionAttempted = true, packageResolutionResult = resolution.Status.ToString(), packageId = state.PackageId }, JsonOptions));
        }

        var partialState = new PanelIdentityCaseState(candidate?.PixelPitch, candidate?.PanelCode, candidate?.SecondaryCode, null,
            source, "NotAttempted", null, DateTimeOffset.UtcNow);
        await PersistAsync(supportCase, partialState, ct);
        var replyText = parsed.Status == PanelIdentityTextStatus.AmbiguousIdentity
            ? "Bu iki değerin hangi alanlara ait olduğunu ayıramadım. P2.5+1065S+2012 formatında paylaşır mısınız?"
            : candidate?.PixelPitch is null
                ? "Pitch bilgisini de almam lazım. P2.5 ise P2.5+1065S+2012 şeklinde yazabilirsiniz."
                : "Son kodu da almam lazım. P2.5+1065S+2012 formatında yazabilirsiniz.";
        return Build(parsed, candidate, "NotAttempted", null, replyText);
    }

    private async Task PersistAsync(TechnicalSupportCase supportCase, PanelIdentityCaseState state, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await db.TechnicalSupportCases.Where(x => x.Id == supportCase.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PanelIdentityStateJson, json), ct);
        supportCase.PanelIdentityStateJson = json;
    }

    private static PanelIdentityTurnResult Build(PanelIdentityTextResult parsed, PanelIdentity? identity, string resolution, Guid? packageId, string reply) =>
        new(true, reply, JsonSerializer.Serialize(new { panelIdentityAttempted = true, panelIdentitySource = parsed.Source,
            panelIdentityComplete = identity?.IsComplete == true, identity?.PixelPitch, identity?.PanelCode, identity?.SecondaryCode,
            canonicalPanelKey = identity?.CanonicalKey, packageResolutionAttempted = false, packageResolutionResult = resolution, packageId }, JsonOptions));

    private static PanelIdentityCaseState? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<PanelIdentityCaseState>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
