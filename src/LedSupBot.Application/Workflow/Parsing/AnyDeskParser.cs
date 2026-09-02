using System.Text.RegularExpressions;

namespace LedSupBot.Application.Workflow.Parsing;

public static partial class AnyDeskParser
{
    public static bool TryParse(string? text, out string normalizedAddress)
    {
        normalizedAddress = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var lower = text.ToLowerInvariant();
        if (lower.Contains('x') || lower.Contains('×') || lower.Contains('*') ||
            lower.Contains("cm") || lower.Contains("metre") ||
            lower.Contains("genişlik") || lower.Contains("yükseklik") ||
            lower.Contains("yatay") || lower.Contains("dikey"))
        {
            return false;
        }

        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (digits.Length is < 9 or > 12)
        {
            return false;
        }

        normalizedAddress = digits;
        return true;
    }

    public static bool LooksLikeAttempt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var lower = text.ToLowerInvariant();
        if (lower.Contains("anydesk") || lower.Contains("any desk") || lower.Contains("enidesk"))
        {
            return true;
        }

        if (lower.Contains('x') || lower.Contains('×') || lower.Contains('*') ||
            lower.Contains("cm") || lower.Contains("metre") ||
            lower.Contains("genişlik") || lower.Contains("yükseklik") ||
            lower.Contains("yatay") || lower.Contains("dikey"))
        {
            return false;
        }

        var digits = new string(text.Where(char.IsDigit).ToArray());
        return digits.Length is >= 5 and <= 8 && Regex.IsMatch(text, @"\d{3}[\s.-]\d{2,3}");
    }
}
