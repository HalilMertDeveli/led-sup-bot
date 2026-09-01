using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;

namespace LedSupBot.Application.Workflow.Context;

public interface IConversationContextBuilder
{
    Task<ConversationContext> BuildAsync(
        Conversation conversation,
        TechnicalSupportCase supportCase,
        WorkflowInboundMessage inbound,
        WorkflowSlotState slots,
        ConversationStage effectiveStage,
        CancellationToken cancellationToken = default);
}
