using System.Text.RegularExpressions;

namespace LedSupBot.Application.Workflow.Parsing;

public static partial class ReceiverCountParser
{
    public static bool TryParse(string? text, out int count)
    {
        count = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var keywordFirst = KeywordFirstPattern().Match(text);
        if (keywordFirst.Success && TryConvert(keywordFirst.Groups[1].Value, out count))
        {
            return true;
        }

        var keywordAfter = KeywordAfterPattern().Match(text);
        if (keywordAfter.Success && TryConvert(keywordAfter.Groups[1].Value, out count))
        {
            return true;
        }

        if (StandalonePattern().IsMatch(text.Trim()) &&
            TryConvert(StandalonePattern().Match(text.Trim()).Groups[1].Value, out count))
        {
            return true;
        }

        return false;
    }

    private static bool TryConvert(string value, out int count)
    {
        count = 0;
        if (!int.TryParse(value, out count) || count <= 0 || count > 999)
        {
            return false;
        }

        return true;
    }

    [GeneratedRegex(@"(?<!\d)(\d{1,3})\s*(?:tane|adet)?\s*(?:receiver|reciver|kart)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeywordFirstPattern();

    [GeneratedRegex(@"(?:receiver|reciver|kart)[^\d]{0,20}(\d{1,3})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeywordAfterPattern();

    [GeneratedRegex(@"^(\d{1,3})\s*(?:tane|adet)?\s*(?:receiver|reciver|kart)?\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StandalonePattern();
}
