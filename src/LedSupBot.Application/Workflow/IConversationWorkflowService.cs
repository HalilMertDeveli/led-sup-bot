using LedSupBot.Domain.Enums;

namespace LedSupBot.Application.Workflow;

public interface IConversationWorkflowService
{
    Task ProcessCustomerInboundAsync(WorkflowInboundMessage inbound, CancellationToken cancellationToken = default);
}

public sealed record WorkflowInboundMessage(
    Guid ConversationId,
    string CustomerPhoneE164,
    Guid MessageId,
    string ExternalMessageId,
    MessageType MessageType,
    string? TextContent,
    IReadOnlyList<Guid>? CoalescedMessageIds = null,
    string? WhatsAppDeliveryRoute = null);
