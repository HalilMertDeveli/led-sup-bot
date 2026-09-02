namespace LedSupBot.Application.Admin;

public interface IAdminEventNotifier
{
    Task NotifyConversationUpdatedAsync(Guid conversationId, CancellationToken cancellationToken = default);

    Task NotifyMessageReceivedAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken = default);

    Task NotifyAnyDeskReadyForHandoffAsync(AnyDeskReadyNotification notification, CancellationToken cancellationToken = default);

    Task NotifyWhatsAppStatusChangedAsync(string status, CancellationToken cancellationToken = default);

    Task NotifyOperatorStateChangedAsync(Guid? conversationId = null, CancellationToken cancellationToken = default);
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

public sealed class ConversationEventPayload
{
    public Guid ConversationId { get; set; }
}

public sealed class MessageEventPayload
{
    public Guid ConversationId { get; set; }
    public Guid MessageId { get; set; }
}

public sealed class WhatsAppStatusEventPayload
{
    public string? Status { get; set; }
}

public sealed class OperatorStateEventPayload
{
    public Guid? ConversationId { get; set; }
    public Guid? MessageId { get; set; }
    public string? Status { get; set; }
}

public sealed class AnyDeskAlertEvent
{
    public Guid ConversationId { get; set; }
    public string? CustomerDisplayName { get; set; }
    public string? IssueSummary { get; set; }
    public string? ScreenSummary { get; set; }
    public string? ReceiverSummary { get; set; }
    public string? PanelStatus { get; set; }
    public string? ConfigStatus { get; set; }
    public string? AnyDeskStatus { get; set; }
}
