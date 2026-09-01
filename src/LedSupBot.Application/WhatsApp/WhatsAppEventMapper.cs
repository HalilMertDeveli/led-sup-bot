using LedSupBot.Application.Abstractions;
using LedSupBot.Domain.Enums;
using LedSupBot.Domain.ValueObjects;

namespace LedSupBot.Application.WhatsApp;

public static class WhatsAppEventMapper
{
    public static MessageType MapMessageType(string? value) => value?.ToLowerInvariant() switch
    {
        "text" => MessageType.Text,
        "image" => MessageType.Image,
        "document" => MessageType.Document,
        "audio" => MessageType.Audio,
        "video" => MessageType.Video,
        "template" => MessageType.Template,
        "system" => MessageType.System,
        _ => MessageType.Other
    };

    public static MessageSender MapSender(bool fromMe, bool botOriginated)
    {
        if (!fromMe)
        {
            return MessageSender.Customer;
        }

        return botOriginated ? MessageSender.Bot : MessageSender.Technician;
    }

    public static MessageDirection MapDirection(bool fromMe) =>
        fromMe ? MessageDirection.Outbound : MessageDirection.Inbound;

    public static WhatsAppConnectionStatus MapConnectionStatus(string? status) =>
        status?.ToUpperInvariant() switch
        {
            "CONNECTED" => WhatsAppConnectionStatus.Connected,
            "CONNECTING" => WhatsAppConnectionStatus.Connecting,
            "AUTHENTICATION_REQUIRED" => WhatsAppConnectionStatus.AuthenticationRequired,
            "ERROR" => WhatsAppConnectionStatus.Error,
            _ => WhatsAppConnectionStatus.Disconnected
        };

    public static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            throw new ArgumentException("Phone number is required.", nameof(phone));
        }

        return new PhoneNumber(phone).Value.TrimStart('+');
    }
}
