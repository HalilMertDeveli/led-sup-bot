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
            var issue = SanitizeIssueSummary(raw.IssueSummary);

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

        if (context.Screen?.Complete == true &&
            ContainsScreenReAsk(trimmed))
        {
            return null;
        }

        return trimmed;
    }

    private static bool ContainsScreenReAsk(string reply)
    {
        var lower = reply.ToLowerInvariant();
        return lower.Contains("ekran ölç") ||
               lower.Contains("ekran olcu") ||
               lower.Contains("ekran boyut");
    }

    private static string? SanitizeIssueSummary(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        var trimmed = summary.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }
}
