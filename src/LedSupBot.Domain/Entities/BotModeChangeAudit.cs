using LedSupBot.Domain.Common;
using LedSupBot.Domain.Enums;

namespace LedSupBot.Domain.Entities;

/// <summary>Minimal audit trail for BotMode transitions (Phase 8).</summary>
public sealed class BotModeChangeAudit : Entity
{
    public Guid ConversationId { get; set; }

    public Conversation Conversation { get; set; } = null!;

    public BotMode PreviousMode { get; set; }

    public BotMode NewMode { get; set; }

    public TakeoverChangeSource Source { get; set; }

    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}
