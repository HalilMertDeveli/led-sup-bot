using LedSupBot.Domain.Enums;

namespace LedSupBot.Application.WhatsApp;

/// <summary>
/// Deterministic gate for future automated replies (Phase 5+).
/// Phase 4.5 has no auto-replies; this encodes the rule so it cannot be ignored later.
/// </summary>
public static class ConversationBotPolicy
{
    public static bool CanAutoReply(BotMode mode) => mode == BotMode.Bot;

    /// <summary>
    /// Resolves whether an outbound/fromMe WhatsApp event should be treated as bot-authored.
    /// Prefer explicit connector flag; fall back to a previously persisted Bot message with the same external id.
    /// </summary>
    public static bool IsBotOriginated(bool connectorBotOriginatedFlag, bool persistedBotMessageExists) =>
        connectorBotOriginatedFlag || persistedBotMessageExists;
}
