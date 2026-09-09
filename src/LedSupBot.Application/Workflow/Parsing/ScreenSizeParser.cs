using System.Globalization;
using System.Text.RegularExpressions;

namespace LedSupBot.Application.Workflow.Parsing;

public static partial class ScreenSizeParser
{
    private const string WidthLabelPattern =
        @"(?:yatayda|yataydaki|yatay|genişliğinde|genisliginde|genişlikte|genislikte|genişliği|genisligi|genişlik|genislik|eninde|enine|eni|en|width|w)";

    private const string HeightLabelPattern =
        @"(?:dikeyde|dikeydeki|dikey|dikeu|yüksekliğinde|yuksekliginde|yükseklikte|yukseklikte|yüksekliği|yuksekligi|yükseklik|yukseklik|yukseklk|boyunda|boyuna|boyu|boy|height|h)";

    public static ScreenSizeParseResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ScreenSizeParseResult.Empty;
        }

        var normalized = text.Trim().ToLower(new CultureInfo("tr-TR"));

        if (IsHumanOrUnrelatedMeasurement(normalized))
        {
            return ScreenSizeParseResult.Empty;
        }

        // 1. Explicit labeled dimensions (e.g. "Dikeyde 160 cm, yatayda 320 cm", "genişlik 160 yükseklik 192", "boy 160 en 320")
        var labeled = TryLabeledDimensions(normalized);
        if (labeled is not null)
        {
            return labeled;
        }

        // 2. Explicit meter pairs (e.g. "5 metre x 3 metre", "5m x 3m", "3.2 metre en 1.6 metre boy")
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

        // 4. Dimension pairs with separator 'x', '×', '*', 'ye', 'e' (e.g. "192x160", "500x300", "320 ye 160", "320'ye 160")
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

        // 6. Single dimension with label (e.g. "Yatay 500 cm", "Dikey 300 cm", "en 320", "boy 160")
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

    public static ScreenSizeParseResult ParseWithContext(string? text, decimal? knownWidth, decimal? knownHeight)
    {
        var parsed = Parse(text);
        if (parsed.ParsedAny)
        {
            return parsed;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return ScreenSizeParseResult.Empty;
        }

        var normalized = text.Trim().ToLower(new CultureInfo("tr-TR"));

        if (IsHumanOrUnrelatedMeasurement(normalized))
        {
            return ScreenSizeParseResult.Empty;
        }

        // If one dimension is already known and customer provides a single standalone number
        var match = Regex.Match(normalized, @"^(?:olarak\s+)?(?<!\d)(\d+(?:[.,]\d+)?)\s*(cm|centimetre|santimetre|m|metre|meter)?\.?$", RegexOptions.CultureInvariant);
        if (match.Success)
        {
            var val = ToCentimeters(ParseNumber(match.Groups[1].Value), match.Groups[2].Value);
            if (val is > 0)
            {
                if (knownWidth is > 0 && (knownHeight is null or <= 0))
                {
                    return new ScreenSizeParseResult
                    {
                        ParsedAny = true,
                        WidthCm = null,
                        HeightCm = val,
                    };
                }

                if (knownHeight is > 0 && (knownWidth is null or <= 0))
                {
                    return new ScreenSizeParseResult
                    {
                        ParsedAny = true,
                        WidthCm = val,
                        HeightCm = null,
                    };
                }
            }
        }

        return ScreenSizeParseResult.Empty;
    }

    private static bool IsHumanOrUnrelatedMeasurement(string text)
    {
        // "boyum 180 cm", "benim boyum 180", "kilom 80", "yaşım 30", "2+2 kaç"
        if (Regex.IsMatch(text, @"\b(?:boyum|kilom|kilo|yaşım|yasim|yaşındayım|yasindayim|kişiyiz|kisiyiz|fiyat|ücret|ucret|lira|tl|hava|derece|nüfus|nufus|hayvan|başkent|baskent|şaka|saka|yemek)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            // If it doesn't explicitly mention ekran / panel / modül / kasa
            return !Regex.IsMatch(text, @"\b(?:ekran|panel|modül|modul|kasa)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        // Math expressions like 2+2, 5*5, 10/2 (without dimension context)
        if (Regex.IsMatch(text, @"\d+\s*[\+\/\=]\s*\d+", RegexOptions.CultureInvariant))
        {
            return true;
        }

        return false;
    }

    private static ScreenSizeParseResult? TryLabeledDimensions(string text)
    {
        var firstNumMatch = Regex.Match(text, @"\d+(?:[.,]\d+)?", RegexOptions.CultureInvariant);
        var firstLabelMatch = Regex.Match(text, $@"\b({WidthLabelPattern}|{HeightLabelPattern})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!firstNumMatch.Success || !firstLabelMatch.Success)
        {
            return null;
        }

        decimal? width = null;
        decimal? height = null;

        var isNumberFirst = firstNumMatch.Index < firstLabelMatch.Index;

        if (isNumberFirst)
        {
            // Number first: "160 cm dikeyde", "320 cm yatayda", "320 genişlik 160 yükseklik", "3.2 metre en 1.6 metre boy"
            var numFirstPattern = $@"(?<!\d)(\d+(?:[.,]\d+)?)\s*(cm|centimetre|santimetre|m|metre|meter)?\s*(?:(?:lik|likte|lık|lik|de|da|e|ye|a|ya)\s+)?\b({WidthLabelPattern}|{HeightLabelPattern})\b";
            var numFirstMatches = Regex.Matches(text, numFirstPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            foreach (Match m in numFirstMatches)
            {
                var val = ToCentimeters(ParseNumber(m.Groups[1].Value), m.Groups[2].Value);
                var label = m.Groups[3].Value.ToLower(new CultureInfo("tr-TR"));
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
            // Label first: "dikeyde 160 cm", "genişlik: 320", "en 320 boy 160", "yükseklik 192 cm"
            var labelFirstPattern = $@"\b({WidthLabelPattern}|{HeightLabelPattern})\s*[:=]?\s*(?:olarak\s+)?(?<!\d)(\d+(?:[.,]\d+)?)\s*(cm|centimetre|santimetre|m|metre|meter)?\b";
            var labelFirstMatches = Regex.Matches(text, labelFirstPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            foreach (Match m in labelFirstMatches)
            {
                var label = m.Groups[1].Value.ToLower(new CultureInfo("tr-TR"));
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
        Regex.IsMatch(label, $"^{WidthLabelPattern}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsHeightLabel(string label) =>
        Regex.IsMatch(label, $"^{HeightLabelPattern}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
        var widthOnlyPattern = $@"^(?:{WidthLabelPattern})\s*[:=]?\s*(?:olarak\s+)?(?<!\d)(\d+(?:[.,]\d+)?)\s*(cm|centimetre|santimetre|m|metre|meter)?\.?$";
        var widthOnly = Regex.Match(text, widthOnlyPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (widthOnly.Success)
        {
            return new ScreenSizeParseResult
            {
                ParsedAny = true,
                WidthCm = ToCentimeters(ParseNumber(widthOnly.Groups[1].Value), widthOnly.Groups[2].Value),
            };
        }

        var heightOnlyPattern = $@"^(?:{HeightLabelPattern})\s*[:=]?\s*(?:olarak\s+)?(?<!\d)(\d+(?:[.,]\d+)?)\s*(cm|centimetre|santimetre|m|metre|meter)?\.?$";
        var heightOnly = Regex.Match(text, heightOnlyPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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

    [GeneratedRegex(@"(?<!\d)(\d+(?:[.,]\d+)?)\s*(?:cm|centimetre|santimetre)?\s*(?:[x×*]|(?:'?(?:ye|e|ya|a)\b))\s*(\d+(?:[.,]\d+)?)\s*(?:cm|centimetre|santimetre)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CentimeterPairPattern();

    [GeneratedRegex(@"\b(\d+(?:[.,]\d+)?)\s*(?:m|metre|meter)\s*(?:[x×*]|(?:'?(?:ye|e|ya|a)\b)|ve\s+)\s*(\d+(?:[.,]\d+)?)\s*(?:m|metre|meter)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MeterPairPattern();

    [GeneratedRegex(@"(?<!\d)(\d+(?:[.,]\d+)?)\s*(?:[x×*]|(?:'?(?:ye|e|ya|a)\b))\s*(\d+(?:[.,]\d+)?)(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PairPattern();

    [GeneratedRegex(@"^(?:ekran\s+)?(?<!\d)(\d+(?:[.,]\d+)?)\s+(\d+(?:[.,]\d+)?)\s*(?:cm|centimetre|santimetre)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpacePairPattern();

    [GeneratedRegex(@"\b(?:\d+(?:[.,]\d+)?)\s*(?:m|metre|meter)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MeterUnitPattern();
}
