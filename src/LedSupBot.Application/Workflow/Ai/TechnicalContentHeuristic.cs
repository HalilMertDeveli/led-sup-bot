using System.Text.RegularExpressions;

namespace LedSupBot.Application.Workflow.Ai;

/// <summary>Lightweight heuristics for cost control — not authoritative parsing.</summary>
public static partial class TechnicalContentHeuristic
{
    public static bool MightContainTechnicalInfo(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return MightContainScreenInfo(text) ||
               MightContainReceiverInfo(text) ||
               MightContainConfigInfo(text) ||
               MightContainAnyDeskInfo(text) ||
               DigitPattern().IsMatch(text);
    }

    public static bool MightContainScreenInfo(string text) =>
        Regex.IsMatch(text, @"\d\s*[x×'/\-\s]\s*\d|\bekran\b|\bölçü\b|\bolcu\b|\bcm\b|\bmetre\b|\bm\b|\byatay\b|\bdikey\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool MightContainReceiverInfo(string text) =>
        Regex.IsMatch(text, @"\breceiver\b|\breciever\b|\breciver\b|\byeşil\s+kart\b|\byesil\s+kart\b|\bkart\b|\badet\b|\btane\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool MightContainConfigInfo(string text) =>
        Regex.IsMatch(text, @"\bevet\b|\bhayır\b|\bhayir\b|\bbağla|\bagla|\bconfig\b|\byazıcı\s+kabl|\byazici\s+kabl", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool MightContainAnyDeskInfo(string text) =>
        Regex.IsMatch(text, @"\banydesk\b|\badres\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
        Regex.IsMatch(text, @"\d{3}[\s.-]?\d{3}[\s.-]?\d{3}", RegexOptions.CultureInvariant);

    [GeneratedRegex(@"\d", RegexOptions.CultureInvariant)]
    private static partial Regex DigitPattern();
}
