using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Application.Workflow.Context;

namespace LedSupBot.Infrastructure.OpenAi;

public sealed class UnavailableAiConversationPlanner : IAiConversationPlanner
{
    public Task<AiResponsePlanRaw?> TryPlanAsync(
        ConversationContext context,
        string latestCustomerMessage,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<AiResponsePlanRaw?>(null);
}
