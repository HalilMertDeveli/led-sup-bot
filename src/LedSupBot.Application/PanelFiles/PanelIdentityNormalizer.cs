using System.Globalization;
using System.Text.RegularExpressions;
using LedSupBot.Domain.PanelFiles;

namespace LedSupBot.Application.PanelFiles;

public enum PanelIdentityNormalizationStatus { Valid = 0, MissingInformation = 1, Malformed = 2 }
public sealed record PanelIdentityNormalizationResult(PanelIdentityNormalizationStatus Status, PanelIdentity? Identity, string? Error = null);

public interface IPanelIdentityNormalizer
{
    PanelIdentityNormalizationResult Normalize(string? pixelPitch, string? panelCode, string? secondaryCode);
    PanelIdentityNormalizationResult NormalizeCanonicalInput(string? input);
}

public sealed partial class PanelIdentityNormalizer : IPanelIdentityNormalizer
{
    public PanelIdentityNormalizationResult Normalize(string? pixelPitch, string? panelCode, string? secondaryCode)
    {
        if (string.IsNullOrWhiteSpace(pixelPitch) || string.IsNullOrWhiteSpace(panelCode) || string.IsNullOrWhiteSpace(secondaryCode))
            return new(PanelIdentityNormalizationStatus.MissingInformation, null, "pixel_pitch_panel_code_and_secondary_code_are_required");

        var pitch = pixelPitch.Trim().ToUpperInvariant().Replace(',', '.');
        var code = panelCode.Trim().ToUpperInvariant();
        var secondary = secondaryCode.Trim().ToUpperInvariant();
        if (!PitchRegex().IsMatch(pitch) || !PanelCodeRegex().IsMatch(code) || !SecondaryCodeRegex().IsMatch(secondary))
            return new(PanelIdentityNormalizationStatus.Malformed, null, "panel_identity_is_malformed");

        var number = decimal.Parse(pitch[1..], CultureInfo.InvariantCulture);
        if (number <= 0) return new(PanelIdentityNormalizationStatus.Malformed, null, "pixel_pitch_is_invalid");
        pitch = "P" + number.ToString("0.###", CultureInfo.InvariantCulture);
        return new(PanelIdentityNormalizationStatus.Valid, new PanelIdentity(pitch, code, secondary));
    }

    public PanelIdentityNormalizationResult NormalizeCanonicalInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new(PanelIdentityNormalizationStatus.MissingInformation, null);
        var match = CanonicalInputRegex().Match(input.Trim());
        return match.Success
            ? Normalize(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value)
            : new(PanelIdentityNormalizationStatus.Malformed, null, "panel_identity_is_malformed");
    }

    [GeneratedRegex(@"^P\d+(?:\.\d{1,3})?$")]
    private static partial Regex PitchRegex();
    [GeneratedRegex(@"^[A-Z0-9]{2,32}$")]
    private static partial Regex PanelCodeRegex();
    [GeneratedRegex(@"^[A-Z0-9]{2,32}$")]
    private static partial Regex SecondaryCodeRegex();
    [GeneratedRegex(@"(?i)^\s*(P\d+(?:[.,]\d{1,3})?)\s*(?:\+|-|_|/|\s)\s*([A-Z0-9]{2,32})\s*(?:\+|-|_|/|\s)\s*([A-Z0-9]{2,32})\s*$")]
    private static partial Regex CanonicalInputRegex();
}
