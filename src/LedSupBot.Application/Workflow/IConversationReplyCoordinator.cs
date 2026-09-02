namespace LedSupBot.Application.Workflow;

public sealed record PendingCustomerInbound(
    Guid ConversationId,
    string CustomerPhoneE164,
    Guid MessageId,
    string ExternalMessageId,
    Domain.Enums.MessageType MessageType,
    string? TextContent,
    string? WhatsAppDeliveryRoute = null);

public interface IConversationReplyCoordinator
{
    void EnqueueCustomerInbound(PendingCustomerInbound inbound);

    void CancelPendingReply(Guid conversationId);

    BotReplyActivity GetActivity(Guid conversationId);

    void SetActivity(Guid conversationId, BotReplyActivity activity);

    CancellationToken LinkReplyCancellation(Guid conversationId, CancellationToken callerToken);
}
