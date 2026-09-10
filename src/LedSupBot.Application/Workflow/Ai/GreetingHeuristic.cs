namespace LedSupBot.Application.Workflow.Ai;

public static class GreetingHeuristic
{
    private static readonly string[] GreetingTokens =
    [
        "selam",
        "merhaba",
        "kolay gelsin",
        "iyi günler",
        "iyi gunler",
        "günaydın",
        "gunaydin",
        "hey",
        "slm",
        "mrb",
        "sa",
        "as",
    ];

    public static bool IsGreeting(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim().ToLowerInvariant();
        if (normalized.Length > 80)
        {
            return false;
        }

        return GreetingTokens.Any(token =>
            normalized == token ||
            normalized.StartsWith(token + " ", StringComparison.Ordinal) ||
            normalized.StartsWith(token + ",", StringComparison.Ordinal) ||
            normalized.StartsWith(token + "!", StringComparison.Ordinal));
    }
}
