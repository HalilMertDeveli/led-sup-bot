using System.Globalization;
using System.Text.RegularExpressions;

namespace LedSupBot.Application.Workflow.Parsing;

public static partial class ScreenSizeParser
{
    public static ScreenSizeParseResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ScreenSizeParseResult.Empty;
        }

        var normalized = text.Trim().ToLowerInvariant();
        if (MeterPattern().IsMatch(normalized))
        {
            return new ScreenSizeParseResult { NeedsMeterConfirmation = true, ParsedAny = true };
        }

        var labeled = TryLabeledDimensions(normalized);
        if (labeled is not null)
        {
            return labeled;
        }

        var pair = TryDimensionPair(normalized);
        if (pair is not null)
        {
            return pair;
        }

        var spaced = TrySpaceSeparatedPair(normalized);
        if (spaced is not null)
        {
            return spaced;
        }

        var single = TrySingleDimension(normalized);
        if (single is not null)
        {
            return single;
        }

        if (Regex.IsMatch(normalized, @"\d"))
        {
            return new ScreenSizeParseResult { Ambiguous = true, ParsedAny = true };
        }

        return ScreenSizeParseResult.Empty;
    }

    private static ScreenSizeParseResult? TryLabeledDimensions(string text)
    {
        decimal? width = MatchLabeledDimension(
            text,
            "yatay|genişlik|genislik|width");

        decimal? height = MatchLabeledDimension(
            text,
            "dikey|yükseklik|yukseklik|height");

        if (width is null && height is null)
        {
            return null;
        }

        return new ScreenSizeParseResult
        {
            ParsedAny = true,
            WidthCm = width,
            HeightCm = height,
        };
    }

    private static decimal? MatchLabeledDimension(string text, string labelPattern)
    {
        var labelFirst = Regex.Match(
            text,
            $@"(?:{labelPattern})\s*[:=]?\s*(\d+(?:[.,]\d+)?)\s*(?:cm|centimetre|santimetre)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (labelFirst.Success)
        {
            return ParseNumber(labelFirst.Groups[1].Value);
        }

        var numberFirst = Regex.Match(
            text,
            $@"(\d+(?:[.,]\d+)?)\s*(?:cm|centimetre|santimetre)\s*(?:{labelPattern})(?!\s*[:=]?\s*\d)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (numberFirst.Success)
        {
            return ParseNumber(numberFirst.Groups[1].Value);
        }

        return null;
    }

    private static ScreenSizeParseResult? TryDimensionPair(string text)
    {
        var match = PairPattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        return new ScreenSizeParseResult
        {
            ParsedAny = true,
            WidthCm = ParseNumber(match.Groups[1].Value),
            HeightCm = ParseNumber(match.Groups[2].Value),
        };
    }

    private static ScreenSizeParseResult? TrySpaceSeparatedPair(string text)
    {
        var match = SpacePairPattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        return new ScreenSizeParseResult
        {
            ParsedAny = true,
            WidthCm = ParseNumber(match.Groups[1].Value),
            HeightCm = ParseNumber(match.Groups[2].Value),
        };
    }

    private static ScreenSizeParseResult? TrySingleDimension(string text)
    {
        var widthOnly = Regex.Match(text, @"^(?:yatay|genişlik|genislik|width)\s*[:=]?\s*(\d+(?:[.,]\d+)?)\s*(?:cm|centimetre|santimetre)?$");
        if (widthOnly.Success)
        {
            return new ScreenSizeParseResult
            {
                ParsedAny = true,
                WidthCm = ParseNumber(widthOnly.Groups[1].Value),
            };
        }

        var heightOnly = Regex.Match(text, @"^(?:dikey|yükseklik|yukseklik|height)\s*[:=]?\s*(\d+(?:[.,]\d+)?)\s*(?:cm|centimetre|santimetre)?$");
        if (heightOnly.Success)
        {
            return new ScreenSizeParseResult
            {
                ParsedAny = true,
                HeightCm = ParseNumber(heightOnly.Groups[1].Value),
            };
        }

        return null;
    }

    private static decimal? ParseNumber(string value)
    {
        if (decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        return null;
    }

    [GeneratedRegex(@"\b(\d+(?:[.,]\d+)?)\s*(?:m|metre|meter)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MeterPattern();

    [GeneratedRegex(@"(?<!\d)(\d+(?:[.,]\d+)?)\s*[x×*]\s*(\d+(?:[.,]\d+)?)(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PairPattern();

    [GeneratedRegex(@"(?<!\d)(\d+(?:[.,]\d+)?)\s+(\d+(?:[.,]\d+)?)(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpacePairPattern();
}
