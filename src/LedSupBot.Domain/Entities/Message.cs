using LedSupBot.Domain.Common;
using LedSupBot.Domain.Enums;

namespace LedSupBot.Domain.Entities;

public class Message : Entity
{
    public Guid ConversationId { get; set; }

    public Conversation Conversation { get; set; } = null!;

    public MessageDirection Direction { get; set; }

    public MessageType MessageType { get; set; } = MessageType.Text;

    /// <summary>Text body or caption. Null for media-only messages.</summary>
    public string? Content { get; set; }

    /// <summary>WhatsApp (or other channel) message id for idempotent ingest.</summary>
    public string? ExternalMessageId { get; set; }

    public DeliveryStatus DeliveryStatus { get; set; } = DeliveryStatus.Pending;

    public MessageSender SentBy { get; set; } = MessageSender.Customer;

    /// <summary>Optional object-storage key for inbound/outbound media (Phase 7 fills this).</summary>
    public string? StorageKey { get; set; }

    public ICollection<AiExtractionResult> AiExtractionResults { get; set; } = new List<AiExtractionResult>();
}
