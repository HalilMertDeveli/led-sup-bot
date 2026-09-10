using System.Text.RegularExpressions;
using LedSupBot.Domain.Enums;

namespace LedSupBot.Application.Workflow.Parsing;

public static partial class ReceiverModelParser
{
    public static bool TryParse(string? text, out ReceiverModel model)
    {
        model = ReceiverModel.Unknown;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim();

        // Correction handling: e.g. "pardon e120 değil e80" or "e120 değil e80 kullandım"
        var correctionMatch = CorrectionPattern().Match(normalized);
        if (correctionMatch.Success)
        {
            var target = correctionMatch.Groups[1].Value;
            if (TryParseSingleToken(target, out model))
            {
                return true;
            }
        }

        // Check if multiple matches exist, pick the last mentioned valid model
        var matches = ModelPattern().Matches(normalized);
        if (matches.Count > 0)
        {
            var lastMatch = matches[^1].Value;
            if (TryParseSingleToken(lastMatch, out model))
            {
                return true;
            }
        }

        return false;
    }

    public static ReceiverModel Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ReceiverModel.Unknown;
        }

        if (TryParse(value, out var model))
        {
            return model;
        }

        var cleaned = value.Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "");
        return cleaned switch
        {
            "E120" => ReceiverModel.E120,
            "E80" => ReceiverModel.E80,
            "5A75E" => ReceiverModel._5A75E,
            "E320PRO" or "E320" => ReceiverModel.E320Pro,
            _ => ReceiverModel.Unknown
        };
    }

    public static bool TryParseSingleToken(string token, out ReceiverModel model)
    {
        var cleaned = token.Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "");
        if (cleaned == "E120")
        {
            model = ReceiverModel.E120;
            return true;
        }

        if (cleaned == "E80")
        {
            model = ReceiverModel.E80;
            return true;
        }

        if (cleaned == "5A75E")
        {
            model = ReceiverModel._5A75E;
            return true;
        }

        if (cleaned is "E320PRO" or "E320")
        {
            model = ReceiverModel.E320Pro;
            return true;
        }

        model = ReceiverModel.Unknown;
        return false;
    }

    public static string ToDisplayString(ReceiverModel model) => model switch
    {
        ReceiverModel.E120 => "E120",
        ReceiverModel.E80 => "E80",
        ReceiverModel._5A75E => "5A75E",
        ReceiverModel.E320Pro => "E320 Pro",
        _ => ""
    };

    [GeneratedRegex(@"değil(?:dir)?\s*,?\s*([eE]\s*120|[eE]\s*80|5\s*[aA][\s\-]*75[\s\-]*[eE]|[eE]\s*320\s*(?:pro)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CorrectionPattern();

    [GeneratedRegex(@"\b(?:[eE]\s*120|[eE]\s*80|5\s*[aA][\s\-]*75[\s\-]*[eE]|[eE]\s*320\s*(?:pro)?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ModelPattern();
}
