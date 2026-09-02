using LedSupBot.Application.Admin;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace LedSupBot.Api.Admin;

public sealed class AdminHub : Hub
{
    public Task SubscribeConversation(Guid conversationId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, ConversationGroup(conversationId));

    public Task UnsubscribeConversation(Guid conversationId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, ConversationGroup(conversationId));

    internal static string ConversationGroup(Guid conversationId) => $"conversation:{conversationId:D}";
}

public sealed class SignalRAdminEventNotifier : IAdminEventNotifier
{
    private readonly IHubContext<AdminHub> _hub;
    private readonly ILogger<SignalRAdminEventNotifier> _logger;

    public SignalRAdminEventNotifier(IHubContext<AdminHub> hub, ILogger<SignalRAdminEventNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task NotifyConversationUpdatedAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new ConversationEventPayload { ConversationId = conversationId };
            await _hub.Clients.All.SendAsync("ConversationUpdated", payload, cancellationToken);
            await _hub.Clients.All.SendAsync("OperatorStateChanged", new OperatorStateEventPayload { ConversationId = conversationId }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast ConversationUpdated for {ConversationId}", conversationId);
        }
    }

    public async Task NotifyMessageReceivedAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new MessageEventPayload { ConversationId = conversationId, MessageId = messageId };
            await _hub.Clients.All.SendAsync("MessageReceived", payload, cancellationToken);
            await _hub.Clients.All.SendAsync("OperatorStateChanged", new OperatorStateEventPayload { ConversationId = conversationId, MessageId = messageId }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast MessageReceived for {ConversationId}", conversationId);
        }
    }

    public async Task NotifyWhatsAppStatusChangedAsync(string status, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new WhatsAppStatusEventPayload { Status = status };
            await _hub.Clients.All.SendAsync("WhatsAppStatusChanged", payload, cancellationToken);
            await _hub.Clients.All.SendAsync("OperatorStateChanged", new OperatorStateEventPayload { Status = status }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast WhatsAppStatusChanged for {Status}", status);
        }
    }

    public async Task NotifyOperatorStateChangedAsync(Guid? conversationId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new OperatorStateEventPayload { ConversationId = conversationId };
            await _hub.Clients.All.SendAsync("OperatorStateChanged", payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast OperatorStateChanged for {ConversationId}", conversationId);
        }
    }

    public async Task NotifyAnyDeskReadyForHandoffAsync(AnyDeskReadyNotification notification, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new AnyDeskAlertEvent
            {
                ConversationId = notification.ConversationId,
                CustomerDisplayName = notification.CustomerDisplayName,
                IssueSummary = notification.IssueSummary,
                ScreenSummary = notification.ScreenSummary,
                ReceiverSummary = notification.ReceiverSummary,
                PanelStatus = notification.PanelStatus,
                ConfigStatus = notification.ConfigStatus,
                AnyDeskStatus = notification.AnyDeskStatus,
            };

            await _hub.Clients.All.SendAsync("AnyDeskReadyForHandoff", payload, cancellationToken);
            await _hub.Clients.All.SendAsync("OperatorStateChanged", new OperatorStateEventPayload { ConversationId = notification.ConversationId }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast AnyDeskReadyForHandoff for {ConversationId}", notification.ConversationId);
        }
    }
}
