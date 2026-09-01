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

        var labeled = TryLabeledDimensions(normalized);
        if (labeled is not null)
        {
            return labeled;
        }

        var meterPair = TryMeterPair(normalized);
        if (meterPair is not null)
        {
            return meterPair;
        }

        var cmPair = TryCentimeterPair(normalized);
        if (cmPair is not null)
        {
            return cmPair;
        }

        if (!HasExplicitCentimeterUnit(normalized))
        {
            var pair = TryDimensionPair(normalized);
            if (pair is not null)
            {
                return new ScreenSizeParseResult { Ambiguous = true, ParsedAny = true };
            }

            var spaced = TrySpaceSeparatedPair(normalized);
            if (spaced is not null)
            {
                return new ScreenSizeParseResult { Ambiguous = true, ParsedAny = true };
            }
        }
        else
        {
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
        }

        var single = TrySingleDimension(normalized);
        if (single is not null)
        {
            return single;
        }

        if (HasExplicitMeterUnit(normalized))
        {
            return new ScreenSizeParseResult { NeedsMeterConfirmation = true, ParsedAny = true };
        }

        if (Regex.IsMatch(normalized, @"\d"))
        {
            return new ScreenSizeParseResult { Ambiguous = true, ParsedAny = true };
        }

        return ScreenSizeParseResult.Empty;
    }

    private static ScreenSizeParseResult? TryLabeledDimensions(string text)
    {
        decimal? width = MatchLabeledDimension(text, "yatay|genişlik|genislik|width");
        decimal? height = MatchLabeledDimension(text, "dikey|yükseklik|yukseklik|height");

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
            $@"(?:{labelPattern})\s*[:=]?\s*(\d+(?:[.,]\d+)?)\s*(cm|centimetre|santimetre|m|metre|meter)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (labelFirst.Success)
        {
            return ToCentimeters(ParseNumber(labelFirst.Groups[1].Value), labelFirst.Groups[2].Value);
        }

        var numberFirstCm = Regex.Match(
            text,
            $@"(\d+(?:[.,]\d+)?)\s*(?:cm|centimetre|santimetre)\s*(?:{labelPattern})(?!\s*[:=]?\s*\d)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (numberFirstCm.Success)
        {
            return ParseNumber(numberFirstCm.Groups[1].Value);
        }

        var numberFirstMeter = Regex.Match(
            text,
            $@"(\d+(?:[.,]\d+)?)\s*(?:m|metre|meter)\s*(?:{labelPattern})(?!\s*[:=]?\s*\d)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (numberFirstMeter.Success)
        {
            var meters = ParseNumber(numberFirstMeter.Groups[1].Value);
            return meters is null ? null : ScreenDimensionUnitConverter.FromMeters(meters.Value);
        }

        return null;
    }

    private static ScreenSizeParseResult? TryMeterPair(string text)
    {
        var match = MeterPairPattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var widthM = ParseNumber(match.Groups[1].Value);
        var heightM = ParseNumber(match.Groups[2].Value);
        if (widthM is null or <= 0 || heightM is null or <= 0)
        {
            return null;
        }

        return new ScreenSizeParseResult
        {
            ParsedAny = true,
            WidthCm = ScreenDimensionUnitConverter.FromMeters(widthM.Value),
            HeightCm = ScreenDimensionUnitConverter.FromMeters(heightM.Value),
        };
    }

    private static ScreenSizeParseResult? TryCentimeterPair(string text)
    {
        var match = CentimeterPairPattern().Match(text);
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
        var widthOnly = Regex.Match(text, @"^(?:yatay|genişlik|genislik|width)\s*[:=]?\s*(\d+(?:[.,]\d+)?)\s*(cm|centimetre|santimetre|m|metre|meter)?$");
        if (widthOnly.Success)
        {
            return new ScreenSizeParseResult
            {
                ParsedAny = true,
                WidthCm = ToCentimeters(ParseNumber(widthOnly.Groups[1].Value), widthOnly.Groups[2].Value),
            };
        }

        var heightOnly = Regex.Match(text, @"^(?:dikey|yükseklik|yukseklik|height)\s*[:=]?\s*(\d+(?:[.,]\d+)?)\s*(cm|centimetre|santimetre|m|metre|meter)?$");
        if (heightOnly.Success)
        {
            return new ScreenSizeParseResult
            {
                ParsedAny = true,
                HeightCm = ToCentimeters(ParseNumber(heightOnly.Groups[1].Value), heightOnly.Groups[2].Value),
            };
        }

        return null;
    }

    private static decimal? ToCentimeters(decimal? value, string unitGroup)
    {
        if (value is null or <= 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(unitGroup))
        {
            return value;
        }

        if (ScreenDimensionUnitConverter.IsMeterUnit(unitGroup))
        {
            return ScreenDimensionUnitConverter.FromMeters(value.Value);
        }

        return value;
    }

    private static decimal? ParseNumber(string value)
    {
        if (decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        return null;
    }

    private static bool HasExplicitCentimeterUnit(string text) =>
        text.Contains("cm", StringComparison.Ordinal) ||
        text.Contains("centimetre", StringComparison.Ordinal) ||
        text.Contains("santimetre", StringComparison.Ordinal);

    private static bool HasExplicitMeterUnit(string text) =>
        MeterUnitPattern().IsMatch(text);

    [GeneratedRegex(@"(?<!\d)(\d+(?:[.,]\d+)?)\s*cm\s*[x×*]\s*(\d+(?:[.,]\d+)?)\s*cm", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CentimeterPairPattern();

    [GeneratedRegex(@"\b(\d+(?:[.,]\d+)?)\s*(?:m|metre|meter)\s*[x×*]\s*(\d+(?:[.,]\d+)?)\s*(?:m|metre|meter)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MeterPairPattern();

    [GeneratedRegex(@"(?<!\d)(\d+(?:[.,]\d+)?)\s*[x×*]\s*(\d+(?:[.,]\d+)?)(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PairPattern();

    [GeneratedRegex(@"(?<!\d)(\d+(?:[.,]\d+)?)\s+(\d+(?:[.,]\d+)?)(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpacePairPattern();

    [GeneratedRegex(@"\b(\d+(?:[.,]\d+)?)\s*(?:m|metre|meter)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MeterUnitPattern();
}
