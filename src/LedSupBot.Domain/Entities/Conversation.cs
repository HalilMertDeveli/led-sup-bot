using LedSupBot.Domain.Common;
using LedSupBot.Domain.Enums;

namespace LedSupBot.Domain.Entities;

public class Conversation : Entity
{
    public Guid CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    public ConversationStatus Status { get; set; } = ConversationStatus.Active;

    public BotMode BotMode { get; set; } = BotMode.Bot;

    public ConversationStage CurrentStage { get; set; } = ConversationStage.Start;

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ClosedAt { get; set; }

    public DateTimeOffset? LastCustomerMessageAt { get; set; }

    /// <summary>Exact normalized WhatsApp JID from the last customer inbound (e.g. live @lid).</summary>
    public string? LastInboundWhatsAppAddress { get; set; }

    /// <summary>Trusted WhatsApp routing address for outbound replies (same as last inbound route).</summary>
    public string? WhatsAppRouteJid
    {
        get => LastInboundWhatsAppAddress;
        set => LastInboundWhatsAppAddress = value;
    }

    public ICollection<Message> Messages { get; set; } = new List<Message>();

    public ICollection<TechnicalSupportCase> TechnicalSupportCases { get; set; } = new List<TechnicalSupportCase>();

    public ICollection<AiExtractionResult> AiExtractionResults { get; set; } = new List<AiExtractionResult>();
}
