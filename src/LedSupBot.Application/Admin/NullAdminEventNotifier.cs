namespace LedSupBot.Application.Admin;

public sealed class NullAdminEventNotifier : IAdminEventNotifier
{
    public Task NotifyConversationUpdatedAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task NotifyMessageReceivedAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task NotifyAnyDeskReadyForHandoffAsync(AnyDeskReadyNotification notification, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task NotifyWhatsAppStatusChangedAsync(string status, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task NotifyOperatorStateChangedAsync(Guid? conversationId = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task NotifyPanelPackagesChangedAsync(Guid packageId, string change, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
