namespace LedSupBot.Application.Admin;

public interface IAdminEventNotifier
{
    Task NotifyConversationUpdatedAsync(Guid conversationId, CancellationToken cancellationToken = default);

    Task NotifyMessageReceivedAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken = default);

    Task NotifyAnyDeskReadyForHandoffAsync(AnyDeskReadyNotification notification, CancellationToken cancellationToken = default);
}

public sealed record AnyDeskReadyNotification(
    Guid ConversationId,
    string CustomerDisplayName,
    string? IssueSummary,
    string? ScreenSummary,
    string? ReceiverSummary,
    string PanelStatus,
    string ConfigStatus,
    string AnyDeskStatus);
