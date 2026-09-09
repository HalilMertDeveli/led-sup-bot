namespace LedSupBot.Application.Workflow.Ai;

public static class IssueSummaryMerger
{
    public static string? Merge(string? existing, string? proposed)
    {
        if (string.IsNullOrWhiteSpace(proposed))
        {
            return null;
        }

        var next = proposed.Trim();
        if (next.Length > 500)
        {
            next = next[..500];
        }

        if (string.IsNullOrWhiteSpace(existing))
        {
            return next;
        }

        var current = existing.Trim();
        if (string.Equals(current, next, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (next.Contains(current, StringComparison.OrdinalIgnoreCase))
        {
            return next;
        }

        if (current.Contains(next, StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        if (next.Length <= 120)
        {
            var combined = $"{current}; {next}";
            return combined.Length <= 500 ? combined : current;
        }

        return next;
    }
}
