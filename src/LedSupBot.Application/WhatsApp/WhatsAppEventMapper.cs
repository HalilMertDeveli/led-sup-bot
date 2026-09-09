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
            "DEGRADED_SESSION" => WhatsAppConnectionStatus.DegradedSession,
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

    /// <summary>
    /// Resolves the customer channel address from connector metadata.
    /// Prefers E.164 when available; falls back to full WhatsApp JID (e.g. LID chats).
    /// Group JIDs (@g.us) never resolve to a customer address.
    /// </summary>
    public static string? ResolveCustomerAddress(WhatsAppInboundEvent inbound)
    {
        if (IsGroupJid(inbound.RemoteJid))
        {
            return null;
        }

        if (IsLidJid(inbound.RemoteJid))
        {
            return NormalizeWhatsAppAddress(inbound.RemoteJid!);
        }

        if (!string.IsNullOrWhiteSpace(inbound.RemotePhoneNumber))
        {
            return NormalizePhone(inbound.RemotePhoneNumber);
        }

        if (!string.IsNullOrWhiteSpace(inbound.RemoteJid) && inbound.RemoteJid.Contains('@'))
        {
            return inbound.RemoteJid.Trim();
        }

        return null;
    }

    public static bool IsLidJid(string? jid)
    {
        if (string.IsNullOrWhiteSpace(jid) || !jid.Contains('@'))
        {
            return false;
        }

        var domain = jid.Split('@')[^1].Split(':')[0];
        return domain.StartsWith("lid", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Canonical WhatsApp address for persistence and lookup (strips device suffixes from JID user parts).
    /// </summary>
    public static string NormalizeWhatsAppAddress(string address)
    {
        var trimmed = address.Trim();
        if (!trimmed.Contains('@'))
        {
            return trimmed;
        }

        var parts = trimmed.Split('@', 2);
        var user = parts[0].Split(':')[0];
        var domain = parts[1].Split(':')[0];
        return $"{user}@{domain}";
    }

    /// <summary>
    /// Baileys group chat JID (e.g. 1234567890-123456789@g.us).
    /// </summary>
    public static bool IsGroupJid(string? jidOrAddress)
    {
        if (string.IsNullOrWhiteSpace(jidOrAddress) || !jidOrAddress.Contains('@'))
        {
            return false;
        }

        var domain = jidOrAddress.Split('@')[^1].Split(':')[0];
        return domain.Equals("g.us", StringComparison.OrdinalIgnoreCase);
    }
}
