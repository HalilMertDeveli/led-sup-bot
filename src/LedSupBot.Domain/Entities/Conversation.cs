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

    public ICollection<Message> Messages { get; set; } = new List<Message>();

    public TechnicalSupportCase? TechnicalSupportCase { get; set; }

    public ICollection<AiExtractionResult> AiExtractionResults { get; set; } = new List<AiExtractionResult>();
}
