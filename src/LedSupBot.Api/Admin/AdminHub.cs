using LedSupBot.Application.Admin;
using Microsoft.AspNetCore.SignalR;

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

    public SignalRAdminEventNotifier(IHubContext<AdminHub> hub) => _hub = hub;

    public async Task NotifyConversationUpdatedAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        await _hub.Clients.All.SendAsync("ConversationUpdated", new { conversationId }, cancellationToken);
        await _hub.Clients.Group(AdminHub.ConversationGroup(conversationId))
            .SendAsync("ConversationUpdated", new { conversationId }, cancellationToken);
    }

    public async Task NotifyMessageReceivedAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken = default)
    {
        await _hub.Clients.All.SendAsync("MessageReceived", new { conversationId, messageId }, cancellationToken);
        await _hub.Clients.Group(AdminHub.ConversationGroup(conversationId))
            .SendAsync("MessageReceived", new { conversationId, messageId }, cancellationToken);
    }

    public async Task NotifyAnyDeskReadyForHandoffAsync(AnyDeskReadyNotification notification, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            conversationId = notification.ConversationId,
            customerDisplayName = notification.CustomerDisplayName,
            issueSummary = notification.IssueSummary,
            screenSummary = notification.ScreenSummary,
            receiverSummary = notification.ReceiverSummary,
            panelStatus = notification.PanelStatus,
            configStatus = notification.ConfigStatus,
            anyDeskStatus = notification.AnyDeskStatus,
        };

        await _hub.Clients.All.SendAsync("AnyDeskReadyForHandoff", payload, cancellationToken);
    }
}
