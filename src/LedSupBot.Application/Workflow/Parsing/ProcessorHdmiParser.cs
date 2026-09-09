using System.Text.RegularExpressions;

namespace LedSupBot.Application.Workflow.Parsing;

public enum ProcessorHdmiParseResult
{
    None = 0,
    Confirmed = 1,
    NeedsClarification = 2,
    Negative = 3
}

public static partial class ProcessorHdmiParser
{
    public static ProcessorHdmiParseResult Parse(string? text, bool inHdmiContext = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ProcessorHdmiParseResult.None;
        }

        var normalized = text.Trim();

        // 1. Clarification / "where to plug" questions have highest priority
        if (ClarificationPattern().IsMatch(normalized))
        {
            return ProcessorHdmiParseResult.NeedsClarification;
        }

        // 2. Explicit HDMI connection confirmation (e.g. "hdmi da bağlı", "hdmi bağladım", "hdmi takılı")
        if (ExplicitHdmiConfirmedPattern().IsMatch(normalized))
        {
            return ProcessorHdmiParseResult.Confirmed;
        }

        // 3. General positive confirmation (e.g. "bağladım", "evet bağlı", "bağlı zaten")
        if (GeneralConfirmedPattern().IsMatch(normalized))
        {
            return ProcessorHdmiParseResult.Confirmed;
        }

        // 4. Short positive answers (e.g. "evet", "tamam", "oldu") when already in HDMI context
        if (inHdmiContext && ShortAffirmativePattern().IsMatch(normalized))
        {
            return ProcessorHdmiParseResult.Confirmed;
        }

        // 5. Negative answers
        if (NegativePattern().IsMatch(normalized))
        {
            return ProcessorHdmiParseResult.Negative;
        }

        return ProcessorHdmiParseResult.None;
    }

    [GeneratedRegex(@"(?:nereye|hangi|nasıl|nasil|anlamadım|anlamadim|nere|giriş|giris|çıkış|cikis)\s*(?:tak|bağla|bagla|gidecek|olacak|hdmi)?|\bhdmi\s*(?:nereye|hangi|nasıl|nasil)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClarificationPattern();

    [GeneratedRegex(@"\bhdmi\b.*?\b(?:bağlı|bagli|bağladım|bagladim|bağladık|bagladik|takılı|takili|taktım|taktim|takıldı|takildi|tamam|ok)\b|\b(?:bağlı|bagli|bağladım|bagladim|takılı|takili|taktım|taktim)\b.*?\bhdmi\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitHdmiConfirmedPattern();

    [GeneratedRegex(@"^(?:evet\s*,?\s*)?(?:bağladım|bagladim|bağladık|bagladik|bağlı\s*zaten|bagli\s*zaten|zaten\s*bağlı|zaten\s*bagli|evet\s*bağlı|evet\s*bagli|taktım|taktim|takılı|takili)\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GeneralConfirmedPattern();

    [GeneratedRegex(@"^(?:evet|tamam|tamamdır|tamamdir|oldu|hazır|hazir|ok|yes|bağlı|bagli)\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShortAffirmativePattern();

    [GeneratedRegex(@"^(?:hayır|hayir|yok|bağlamadım|baglamadim|bağlayamadım|baglayamadim|takmadım|takmadim|bağlı\s*değil|bagli\s*degil)\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NegativePattern();
}
