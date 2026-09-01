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

        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (digits.Length is < 9 or > 12)
        {
            return false;
        }

        normalizedAddress = digits;
        return true;
    }

    public static bool LooksLikeAttempt(string? text) =>
        !string.IsNullOrWhiteSpace(text) && DigitPattern().IsMatch(text);

    [GeneratedRegex(@"\d{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex DigitPattern();
}
