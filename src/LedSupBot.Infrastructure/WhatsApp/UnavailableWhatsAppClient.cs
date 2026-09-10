using LedSupBot.Application.Abstractions;

namespace LedSupBot.Infrastructure.WhatsApp;

/// <summary>Fallback when the Node connector is disabled.</summary>
public sealed class UnavailableWhatsAppClient : IWhatsAppClient
{
    public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<WhatsAppConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(WhatsAppConnectionStatus.Disconnected);

    public Task<string?> SendTextMessageAsync(string toPhoneE164, string body, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("WhatsApp connector is not enabled.");

    public Task<string?> SendImageMessageAsync(
        string toPhoneE164,
        Stream image,
        string contentType,
        string? caption = null,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("WhatsApp connector is not enabled.");

    public Task SendComposingAsync(string toPhoneE164, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SendPausedPresenceAsync(string toPhoneE164, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<WhatsAppMediaDownloadResult> DownloadMediaAsync(string mediaId, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("WhatsApp connector is not enabled.");
}
