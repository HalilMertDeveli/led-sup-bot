using LedSupBot.Application.Workflow.Context;

namespace LedSupBot.Application.Workflow.Ai;

public interface IAiConversationPlanner
{
    Task<AiResponsePlanRaw?> TryPlanAsync(
        ConversationContext context,
        string latestCustomerMessage,
        CancellationToken cancellationToken = default);
}

public sealed record AiResponsePlanRaw(
    string Intent,
    string? Reply,
    string? IssueSummary,
    bool NeedsHuman,
    string? SuggestedNextAction);

public sealed record ValidatedAiResponsePlan(
    string Intent,
    string? Reply,
    string? IssueSummary,
    bool NeedsHuman,
    string? SuggestedNextAction)
{
    public bool HasUsableReply => !string.IsNullOrWhiteSpace(Reply);
}

public sealed class AiResponsePlanner
{
    private readonly IAiConversationPlanner _planner;

    public AiResponsePlanner(IAiConversationPlanner planner) => _planner = planner;

    public async Task<ValidatedAiResponsePlan?> TryPlanAsync(
        ConversationContext context,
        string latestCustomerMessage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(latestCustomerMessage))
        {
            return null;
        }

        try
        {
            var raw = await _planner.TryPlanAsync(context, latestCustomerMessage, cancellationToken);
            if (raw is null)
            {
                return null;
            }

            var reply = SanitizeReply(raw.Reply, context);
            var issue = SanitizeIssueSummary(raw.IssueSummary, context.CurrentIssue);

            if (string.IsNullOrWhiteSpace(reply) && string.IsNullOrWhiteSpace(issue))
            {
                return null;
            }

            return new ValidatedAiResponsePlan(
                raw.Intent ?? "unknown",
                reply,
                issue,
                raw.NeedsHuman,
                raw.SuggestedNextAction);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? SanitizeReply(string? reply, ConversationContext context)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return null;
        }

        var trimmed = reply.Trim();
        if (trimmed.Length > 1800)
        {
            trimmed = trimmed[..1800];
        }

        if (context.Screen?.Complete == true && ContainsScreenReAsk(trimmed))
        {
            return null;
        }

        if (context.Receiver?.Complete == true && ContainsReceiverReAsk(trimmed))
        {
            return null;
        }

        if (context.Panel?.Complete == true && ContainsPanelReAsk(trimmed))
        {
            return null;
        }

        if (context.Config?.Complete == true && ContainsConfigReAsk(trimmed))
        {
            return null;
        }

        if (context.AnyDesk?.Complete == true && ContainsAnyDeskReAsk(trimmed))
        {
            return null;
        }

        trimmed = RemoveUnwantedContactPromises(trimmed);

        return trimmed;
    }

    private static string RemoveUnwantedContactPromises(string reply)
    {
        var patterns = new[]
        {
            @"teknik\s+ekibimiz\s+(?:en\s+kısa\s+sürede\s+)?sizinle\s+iletişime\s+geçecek(?:tir)?\.?",
            @"ekibimiz\s+(?:en\s+kısa\s+sürede\s+)?sizinle\s+iletişime\s+geçecek(?:tir)?\.?",
            @"ekibimiz\s+(?:en\s+kısa\s+sürede\s+)?sizinle\s+irtibata\s+geçecek(?:tir)?\.?",
            @"teknik\s+ekibimiz\s+(?:en\s+kısa\s+sürede\s+)?sizi\s+arayacak(?:tır)?\.?",
            @"sizinle\s+iletişime\s+geçeceğiz\.?",
            @"sizinle\s+irtibata\s+geçeceğiz\.?"
        };

        var cleaned = reply;
        foreach (var pattern in patterns)
        {
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                pattern,
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }

        cleaned = cleaned.Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return "Gerekli teknik bilgileri aldım, teşekkürler.";
        }

        return cleaned;
    }

    private static bool ContainsScreenReAsk(string reply)
    {
        var lower = reply.ToLowerInvariant();
        return lower.Contains("ekran ölç") ||
               lower.Contains("ekran olcu") ||
               lower.Contains("ekran boyut") ||
               lower.Contains("en ve boy") ||
               lower.Contains("yatay ve dikey");
    }

    private static bool ContainsReceiverReAsk(string reply)
    {
        var lower = reply.ToLowerInvariant();
        return lower.Contains("receiver") ||
               lower.Contains("reciever") ||
               lower.Contains("yeşil kart") ||
               lower.Contains("yesil kart");
    }

    private static bool ContainsPanelReAsk(string reply)
    {
        var lower = reply.ToLowerInvariant();
        return lower.Contains("panel etiket") ||
               lower.Contains("etiket foto");
    }

    private static bool ContainsConfigReAsk(string reply)
    {
        var lower = reply.ToLowerInvariant();
        return lower.Contains("config kablo") ||
               lower.Contains("yazıcı kablo") ||
               lower.Contains("yazici kablo");
    }

    private static bool ContainsAnyDeskReAsk(string reply)
    {
        var lower = reply.ToLowerInvariant();
        return lower.Contains("anydesk");
    }

    private static string? SanitizeIssueSummary(string? summary, string? existingIssue)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        return IssueSummaryMerger.Merge(existingIssue, summary);
    }
}
