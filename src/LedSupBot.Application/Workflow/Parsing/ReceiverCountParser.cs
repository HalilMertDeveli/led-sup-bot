using System.Globalization;
using System.Text.RegularExpressions;

namespace LedSupBot.Application.Workflow.Parsing;

public static partial class ReceiverCountParser
{
    private const string ReceiverModelPattern =
        @"(?:[eEmM][rR]?[vV]?[-_\s]?\d{2,4}[a-zA-Z]?|[dD][hH][-_\s]?\d{2,4}[a-zA-Z]?|[rR][vV][-_\s]?\d{2,4}[a-zA-Z]?|[aA]\d[sS]?|[tT][bB][-_\s]?\d{1,3}|[vV][xX][-_\s]?\d[sS]?|[rR][-_\s]?\d{2,4}|5a-?75[a-zA-Z]?|5A-?75[a-zA-Z]?)";

    public static bool TryParse(string? text, out int count)
    {
        count = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();

        // 1. Guard against unrelated general knowledge / conversational sentences
        if (IsGeneralOrUnrelatedPhrase(trimmed))
        {
            return false;
        }

        // 2. Correction pattern: e.g. "4 değil 6 tane var", "4 değil 6 receiver"
        var correctionMatch = CorrectionPattern().Match(trimmed);
        if (correctionMatch.Success && TryConvert(correctionMatch.Groups[1].Value, out count))
        {
            return true;
        }

        // 3. Count before model / keyword: e.g. "10 tane e120 kullandım", "4 tane E120 var", "10 adet 5a75e", "8 tane e320 pro", "500x300 cm ve 8 receiver", "8 receiver var", "10 yeşil kart var"
        var countMatches = CountBeforeModelOrKeywordPattern().Matches(trimmed);
        if (countMatches.Count > 0)
        {
            var lastMatch = countMatches[^1];
            if (TryConvert(lastMatch.Groups[1].Value, out count))
            {
                return true;
            }
        }

        // 4. Keyword / Model before count: e.g. "colorlight e120 12 tane", "e120 4 adet", "receiver 8 adet", "receiverlar e120, toplam 6 tane", "kart sayısı 8"
        var modelBeforeCount = ModelOrKeywordBeforeCountPattern().Match(trimmed);
        if (modelBeforeCount.Success &&
            HasReceiverQuantityEvidence(trimmed, modelBeforeCount) &&
            TryConvert(modelBeforeCount.Groups[1].Value, out count))
        {
            return true;
        }

        // 5. Standalone quantity answer: e.g. "8", "8 tane", "8 adet", "toplam 8", "8 tane var", "8 tane kullandım"
        var standaloneMatch = StandalonePattern().Match(trimmed);
        if (standaloneMatch.Success && TryConvert(standaloneMatch.Groups[1].Value, out count))
        {
            return true;
        }

        return false;
    }

    private static bool HasReceiverQuantityEvidence(string text, Match match)
    {
        var countGroup = match.Groups[1];
        var beforeCount = text[match.Index..countGroup.Index];
        var afterCount = text[(countGroup.Index + countGroup.Length)..];

        // A model followed by an unrelated number is not a receiver quantity:
        // "E120 HDMI 2", "E120 5 volt", "E120 81 il".
        if (CompetingNumericContextBeforePattern().IsMatch(beforeCount) ||
            CompetingNumericContextAfterPattern().IsMatch(afterCount))
        {
            return false;
        }

        return QuantityMarkerAfterPattern().IsMatch(afterCount) ||
               QuantityLabelBeforePattern().IsMatch(beforeCount) ||
               ReceiverKeywordBeforePattern().IsMatch(beforeCount);
    }

    private static bool IsGeneralOrUnrelatedPhrase(string text)
    {
        // Avoid false positives like: "Türkiye'de 81 il var mı?", "4 kişiyiz", "2+2 kaç?", "1923 yılında"
        if (Regex.IsMatch(text, @"\b(?:il\b|şehir|sehir|kişi|kisi|insan|yaş\b|yas\b|fiyat|lira|tl|saat|dakika|yıl\b|yil\b|ay\b|gün|gun|derece|soru|cevap|başkent|baskent|hayvan)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            // Unless it explicitly contains receiver / kart / receiver model keyword
            return !Regex.IsMatch(text, $@"\b(?:receiver|reciver|reciever|yeşil\s+kart|yesil\s+kart|alıcı\s+kart|{ReceiverModelPattern})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        // Math expressions like 2+2, 5*5, 10/2
        if (Regex.IsMatch(text, @"\d+\s*[\+\/\=]\s*\d+", RegexOptions.CultureInvariant))
        {
            return true;
        }

        return false;
    }

    private static bool TryConvert(string value, out int count)
    {
        count = 0;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out count) || count <= 0 || count > 999)
        {
            return false;
        }

        return true;
    }

    [GeneratedRegex(@"\b\d+\s*(?:değil|degil)\s*(\d{1,3})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CorrectionPattern();

    [GeneratedRegex(@"(?<![a-zA-Z0-9])(\d{1,3})\s*(?:tane|adet)?\s*(?:(?:yeşil|yesil)\s+kart|receiver|reciver|reciever|kart|e120|e80|5a75e|5a-75e|e320|mrv\d+|rv\d+|dh\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CountBeforeModelOrKeywordPattern();

    [GeneratedRegex(@"(?:receiver|reciver|reciever|yeşil\s+kart|yesil\s+kart|kart|e120|e80|5a75e|5a-75e|e320|mrv\d+|rv\d+|dh\d+)[^\d]{0,25}(?<![a-zA-Z0-9])(\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ModelOrKeywordBeforeCountPattern();

    [GeneratedRegex(@"\b(?:hdmi|volt(?:aj)?|gerilim|piksel|pixel|hz|cm|mm|metre|meter|il|port)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CompetingNumericContextBeforePattern();

    [GeneratedRegex(@"^\s*(?:volt(?:aj)?|v|cm|mm|metre|meter|piksel|pixel|hz|il|port)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CompetingNumericContextAfterPattern();

    [GeneratedRegex(@"^\s*(?:(?:tane|adet)\b|(?:receiver|reciver|reciever|kart)\b(?:\s*(?:var|kullandım|kullandık|kullanıyorum))?|(?:var|kullandım|kullandık|kullanıyorum)\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuantityMarkerAfterPattern();

    [GeneratedRegex(@"\b(?:toplam|sayısı|sayisi)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuantityLabelBeforePattern();

    [GeneratedRegex(@"\b(?:receiver(?:lar)?|reciver(?:lar)?|reciever(?:lar)?|kart(?:lar)?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReceiverKeywordBeforePattern();

    [GeneratedRegex(@"^(?:toplam\s+)?(?<![a-zA-Z0-9])(\d{1,3})\s*(?:tane|adet)?\s*(?:receiver|reciver|reciever|kart|var|kullandım|kullandık)?\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StandalonePattern();
}
