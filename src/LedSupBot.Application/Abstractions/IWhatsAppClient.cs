namespace LedSupBot.Application.Abstractions;

/// <summary>
/// Vendor-agnostic WhatsApp channel. Phase 4.5 uses an unofficial Baileys connector.
/// Not Meta-approved.
/// </summary>
public interface IWhatsAppClient
{
    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<WhatsAppConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default);

    Task<string?> SendTextMessageAsync(string toPhoneE164, string body, CancellationToken cancellationToken = default);

    Task<string?> SendImageMessageAsync(
        string toPhoneE164,
        Stream image,
        string contentType,
        string? caption = null,
        CancellationToken cancellationToken = default);

    Task SendComposingAsync(string toPhoneE164, CancellationToken cancellationToken = default);

    Task SendPausedPresenceAsync(string toPhoneE164, CancellationToken cancellationToken = default);

    Task<WhatsAppMediaDownloadResult> DownloadMediaAsync(string mediaId, CancellationToken cancellationToken = default);
}

public enum WhatsAppConnectionStatus
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    AuthenticationRequired = 3,
    Error = 4
}

public sealed record WhatsAppMediaDownloadResult(
    string MediaId,
    string MimeType,
    byte[] Content);

/// <summary>Inbound event from the Node connector (trusted local channel).</summary>
public sealed class WhatsAppInboundEvent
{
    public string EventType { get; set; } = string.Empty;

    public string? ExternalMessageId { get; set; }

    public string? RemoteJid { get; set; }

    public string? RemotePhoneNumber { get; set; }

    /// <summary>Baileys sender_pn JID when inbound remoteJid is @lid (optional).</summary>
    public string? SenderPnJid { get; set; }

    public bool FromMe { get; set; }

    public bool BotOriginated { get; set; }

    public string? MessageType { get; set; }

    public string? TextContent { get; set; }

    public bool TextPresent { get; set; }

    public DateTimeOffset? Timestamp { get; set; }

    public string? Status { get; set; }

    public WhatsAppMediaMetadata? Media { get; set; }
}

public sealed class WhatsAppMediaMetadata
{
    public string? MimeType { get; set; }

    public long? FileLength { get; set; }

    public string? FileName { get; set; }

    public bool Downloadable { get; set; }
}

public interface IWhatsAppInboundEventHandler
{
    Task HandleAsync(WhatsAppInboundEvent inboundEvent, CancellationToken cancellationToken = default);
}

public interface IWhatsAppConnectionStateStore
{
    WhatsAppConnectionStatus Current { get; }

    void Set(WhatsAppConnectionStatus status);
}
