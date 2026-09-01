namespace LedSupBot.Application.Workflow.Parsing;

public static class ConfigConfirmationParser
{
    private static readonly string[] Positive =
    [
        "evet", "evt", "tamam", "olur", "bağladım", "bagladim", "bağlandı", "baglandi",
        "bağlayabilirim", "baglayabilirim", "yaptım", "yaptim", "hazır", "hazir", "ok",
    ];

    private static readonly string[] Negative =
    [
        "hayır", "hayir", "yok", "olmadı", "olmedi", "bağlayamadım", "baglayamadim", "değil", "degil",
    ];

    public static ConfigConfirmationResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ConfigConfirmationResult.Empty;
        }

        var normalized = text.Trim().ToLowerInvariant();
        if (Positive.Any(p => normalized.Contains(p, StringComparison.Ordinal)))
        {
            return ConfigConfirmationResult.Confirmed;
        }

        if (Negative.Any(n => normalized.Contains(n, StringComparison.Ordinal)))
        {
            return ConfigConfirmationResult.Negative;
        }

        return ConfigConfirmationResult.Unclear;
    }
}

public enum ConfigConfirmationResult
{
    Empty,
    Confirmed,
    Negative,
    Unclear,
}
