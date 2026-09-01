namespace LedSupBot.Application.Admin;

public sealed class NullAdminEventNotifier : IAdminEventNotifier
{
    public Task NotifyConversationUpdatedAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task NotifyMessageReceivedAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task NotifyAnyDeskReadyForHandoffAsync(AnyDeskReadyNotification notification, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
