using System.Globalization;
using System.Text.RegularExpressions;

namespace LedSupBot.Application.Workflow.Parsing;

public enum ConfigConfirmationResult
{
    Empty,
    Confirmed,
    ClarificationRequested,
    Negative,
    Unclear,
}

public static class ConfigConfirmationParser
{
    private static readonly string[] ConfirmedPhrases =
    [
        "bağladım", "bagladim", "bağlandı", "baglandi", "bağladık", "bagladik",
        "taktım", "taktim", "takıldı", "takildi", "taktık", "taktik",
        "bağlı", "bagli", "takılı", "takili",
        "kabloyu bağladım", "kabloyu bagladim", "kabloyu taktım", "kabloyu taktim",
        "evet bağladım", "evet bagladim", "evet taktım", "evet taktim",
        "evet", "evt", "tamam", "olur", "yaptım", "yaptim", "hazır", "hazir", "ok",
    ];

    private static readonly string[] ClarificationPhrases =
    [
        "config kablosu ne", "config kablo ne", "cobfig kablosu ne", "cobfig kablo ne",
        "hangi kablo", "nasıl bir kablo", "nasil bir kablo", "ne kablosu", "kablo ne",
        "nereye bağlayacağım", "nereye baglayacagim", "nereye takacağım", "nereye takacagim",
        "nereye bağlayacam", "nereye baglayacam", "nereye takacam",
        "kablo yok", "kablo nerede", "kablo nerde",
        "nasıl yapacağım", "nasil yapacagim", "nasıl bağlayacağım", "nasil baglayacagim",
        "nasıl takacağım", "nasil takacagim",
        "nereden bulacağım", "nereden bulacagim", "nerden bulacağım", "nerden bulacagim",
        "yazıcı kablosu ne", "yazici kablosu ne", "yazıcı kablo ne",
        "hangi ucu", "hangi uç",
        "anlamadım", "anlamadim", "anlamadık", "anlamadik",
        "ne demek", "nasıl yani", "nasil yani", "bu ne", "ne bu",
        "hangi kablodan bahsediyorsunuz", "kabloyu nereye",
    ];

    private static readonly string[] NegativePhrases =
    [
        "hayır", "hayir", "olmadı", "olmedi", "bağlayamadım", "baglayamadim",
        "takamadım", "takamadim", "kablo yok", "bulamadım", "bulamadim", "yok",
    ];

    public static ConfigConfirmationResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ConfigConfirmationResult.Empty;
        }

        var normalized = text.Trim().ToLower(new CultureInfo("tr-TR"));

        // General chat / question guard: e.g. "tamam da dünyanın en büyük hayvanı ne?", "olur mu sence?"
        if (Regex.IsMatch(normalized, @"\b(?:başkenti|hayvanı|şaka|ingilizce|yemek|film|şarkı|hava|yoruldum|moralim|telefon|araba)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return ConfigConfirmationResult.Unclear;
        }

        if (normalized.Contains('?') &&
            !normalized.Contains("kablo") &&
            !normalized.Contains("config") &&
            !normalized.Contains("bağla") &&
            !normalized.Contains("bagla") &&
            !normalized.Contains("tak"))
        {
            return ConfigConfirmationResult.Unclear;
        }

        // HDMI mentions belong strictly to Processor HDMI flow, not USB config cable
        if (normalized.Contains("hdmi", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("config", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("yazıcı", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("yazici", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("usb", StringComparison.OrdinalIgnoreCase))
        {
            return ConfigConfirmationResult.Unclear;
        }

        // 1. Clarification & Question check MUST run before simple keywords like "evet" or "tamam"
        // (e.g. "tamam da hangi kablo", "config kablosu ne nereye bağlayacağım")
        foreach (var phrase in ClarificationPhrases)
        {
            if (normalized.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return ConfigConfirmationResult.ClarificationRequested;
            }
        }

        if (Regex.IsMatch(normalized, @"\b(?:config|cobfig|kablo|yazıcı|yazici).{0,30}(?:ne|nereye|nasıl|nasil|hangi|nerede|nerde|\?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return ConfigConfirmationResult.ClarificationRequested;
        }

        // 2. Explicit confirmation phrases
        foreach (var phrase in ConfirmedPhrases)
        {
            if (Regex.IsMatch(normalized, $@"\b{Regex.Escape(phrase)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                normalized == phrase)
            {
                return ConfigConfirmationResult.Confirmed;
            }
        }

        // 3. Negative phrases
        foreach (var phrase in NegativePhrases)
        {
            if (Regex.IsMatch(normalized, $@"\b{Regex.Escape(phrase)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                normalized == phrase)
            {
                return ConfigConfirmationResult.Negative;
            }
        }

        return ConfigConfirmationResult.Unclear;
    }
}
