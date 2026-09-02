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

        // 1. Explicit labeled dimensions (e.g. "192cm yükseklik 160 cm genişlik", "genişlik 160 yükseklik 192")
        var labeled = TryLabeledDimensions(normalized);
        if (labeled is not null)
        {
            return labeled;
        }

        // 2. Explicit meter pairs (e.g. "5 metre x 3 metre", "5m x 3m")
        var meterPair = TryMeterPair(normalized);
        if (meterPair is not null)
        {
            return meterPair;
        }

        // 3. Explicit centimeter pairs with unit (e.g. "500 cm x 300 cm", "192x160 cm", "192 x 160 cm")
        var cmPair = TryCentimeterPair(normalized);
        if (cmPair is not null)
        {
            return cmPair;
        }

        // 4. Dimension pairs with separator 'x', '×', '*' (e.g. "192x160", "500x300", "192 x 160")
        var dimPair = TryDimensionPair(normalized);
        if (dimPair is not null)
        {
            return dimPair;
        }

        // 5. Space separated pair (e.g. "500 300 cm", "500 300")
        var spaced = TrySpaceSeparatedPair(normalized);
        if (spaced is not null)
        {
            return spaced;
        }

        // 6. Single dimension with label (e.g. "Yatay 500 cm", "Dikey 300 cm")
        var single = TrySingleDimension(normalized);
        if (single is not null)
        {
            return single;
        }

        // 7. Meter unit indication without full parseable numbers
        if (HasExplicitMeterUnit(normalized))
        {
            return new ScreenSizeParseResult { NeedsMeterConfirmation = true, ParsedAny = true };
        }

        return ScreenSizeParseResult.Empty;
    }

    private static ScreenSizeParseResult? TryLabeledDimensions(string text)
    {
        // First determine if the text uses number-first or label-first structure
        var firstNumMatch = Regex.Match(text, @"\d+(?:[.,]\d+)?", RegexOptions.CultureInvariant);
        var firstLabelMatch = Regex.Match(text, @"\b(yatay|genişlik|genislik|width|dikey|yükseklik|yukseklik|height)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!firstNumMatch.Success || !firstLabelMatch.Success)
        {
            return null;
        }

        decimal? width = null;
        decimal? height = null;

        var isNumberFirst = firstNumMatch.Index < firstLabelMatch.Index;

        if (isNumberFirst)
        {
            // Number-first: "192cm yükseklik 160 cm genişlik", "500 cm yatay 300 cm dikey"
            var numFirstMatches = Regex.Matches(
                text,
                @"(?<!\d)(\d+(?:[.,]\d+)?)\s*(cm|centimetre|santimetre|m|metre|meter)?\s*(?:(?:lik|likte|lık|lik|de|da)\s+)?(yatay|genişlik|genislik|width|dikey|yükseklik|yukseklik|height)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            foreach (Match m in numFirstMatches)
            {
                var val = ToCentimeters(ParseNumber(m.Groups[1].Value), m.Groups[2].Value);
                var label = m.Groups[3].Value.ToLowerInvariant();
                if (IsWidthLabel(label))
                {
                    width = val;
                }
                else if (IsHeightLabel(label))
                {
                    height = val;
                }
            }
        }
        else
        {
            // Label-first: "genişlik 160 yükseklik 192", "yükseklik: 192 cm, genişlik: 160 cm", "Yatay 500 cm dikey 300 cm"
            var labelFirstMatches = Regex.Matches(
                text,
                @"\b(yatay|genişlik|genislik|width|dikey|yükseklik|yukseklik|height)\s*[:=]?\s*(?:olarak\s+)?(\d+(?:[.,]\d+)?)\s*(cm|centimetre|santimetre|m|metre|meter)?\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            foreach (Match m in labelFirstMatches)
            {
                var label = m.Groups[1].Value.ToLowerInvariant();
                var val = ToCentimeters(ParseNumber(m.Groups[2].Value), m.Groups[3].Value);
                if (IsWidthLabel(label))
                {
                    width = val;
                }
                else if (IsHeightLabel(label))
                {
                    height = val;
                }
            }
        }

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

    private static bool IsWidthLabel(string label) =>
        label is "yatay" or "genişlik" or "genislik" or "width";

    private static bool IsHeightLabel(string label) =>
        label is "dikey" or "yükseklik" or "yukseklik" or "height";

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

        var width = ParseNumber(match.Groups[1].Value);
        var height = ParseNumber(match.Groups[2].Value);
        if (width is null or <= 0 || height is null or <= 0)
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

    private static ScreenSizeParseResult? TryDimensionPair(string text)
    {
        var match = PairPattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var width = ParseNumber(match.Groups[1].Value);
        var height = ParseNumber(match.Groups[2].Value);
        if (width is null or <= 0 || height is null or <= 0)
        {
            return null;
        }

        // Numbers >= 10 (or with explicit cm) are treated as centimeters by default (e.g. 192x160, 500x300)
        if (HasExplicitCentimeterUnit(text) || (width >= 10 && height >= 10))
        {
            return new ScreenSizeParseResult
            {
                ParsedAny = true,
                WidthCm = width,
                HeightCm = height,
            };
        }

        // Small numbers without unit (e.g. 5x3) are ambiguous between meters and cm
        return new ScreenSizeParseResult { Ambiguous = true, ParsedAny = true };
    }

    private static ScreenSizeParseResult? TrySpaceSeparatedPair(string text)
    {
        // Must NOT match if text contains 3 or more numbers (like AnyDesk 123 456 789 or phone number)
        var allNumbers = Regex.Matches(text, @"\b\d+(?:[.,]\d+)?\b", RegexOptions.CultureInvariant);
        if (allNumbers.Count != 2)
        {
            return null;
        }

        var match = SpacePairPattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var width = ParseNumber(match.Groups[1].Value);
        var height = ParseNumber(match.Groups[2].Value);
        if (width is null or <= 0 || height is null or <= 0)
        {
            return null;
        }

        if (HasExplicitCentimeterUnit(text) || (width >= 10 && height >= 10))
        {
            return new ScreenSizeParseResult
            {
                ParsedAny = true,
                WidthCm = width,
                HeightCm = height,
            };
        }

        return new ScreenSizeParseResult { Ambiguous = true, ParsedAny = true };
    }

    private static ScreenSizeParseResult? TrySingleDimension(string text)
    {
        var widthOnly = Regex.Match(text, @"^(?:yatay|genişlik|genislik|width)\s*[:=]?\s*(\d+(?:[.,]\d+)?)\s*(cm|centimetre|santimetre|m|metre|meter)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (widthOnly.Success)
        {
            return new ScreenSizeParseResult
            {
                ParsedAny = true,
                WidthCm = ToCentimeters(ParseNumber(widthOnly.Groups[1].Value), widthOnly.Groups[2].Value),
            };
        }

        var heightOnly = Regex.Match(text, @"^(?:dikey|yükseklik|yukseklik|height)\s*[:=]?\s*(\d+(?:[.,]\d+)?)\s*(cm|centimetre|santimetre|m|metre|meter)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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

    [GeneratedRegex(@"(?<!\d)(\d+(?:[.,]\d+)?)\s*(?:cm|centimetre|santimetre)?\s*[x×*]\s*(\d+(?:[.,]\d+)?)\s*(?:cm|centimetre|santimetre)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CentimeterPairPattern();

    [GeneratedRegex(@"\b(\d+(?:[.,]\d+)?)\s*(?:m|metre|meter)\s*[x×*]\s*(\d+(?:[.,]\d+)?)\s*(?:m|metre|meter)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MeterPairPattern();

    [GeneratedRegex(@"(?<!\d)(\d+(?:[.,]\d+)?)\s*[x×*]\s*(\d+(?:[.,]\d+)?)(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PairPattern();

    [GeneratedRegex(@"^\s*(?<!\d)(\d+(?:[.,]\d+)?)\s+(\d+(?:[.,]\d+)?)(?!\d)\s*(?:cm|centimetre|santimetre)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpacePairPattern();

    [GeneratedRegex(@"\b(\d+(?:[.,]\d+)?)\s*(?:m|metre|meter)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MeterUnitPattern();
}
