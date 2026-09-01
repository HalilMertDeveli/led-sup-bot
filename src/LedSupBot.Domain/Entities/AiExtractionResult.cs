using LedSupBot.Domain.Common;
using LedSupBot.Domain.Enums;

namespace LedSupBot.Domain.Entities;

public class AiExtractionResult : Entity
{
    public Guid ConversationId { get; set; }

    public Conversation Conversation { get; set; } = null!;

    public Guid? MessageId { get; set; }

    public Message? Message { get; set; }

    public AiExtractionType ExtractionType { get; set; } = AiExtractionType.SlotExtraction;

    /// <summary>Structured extraction payload stored as JSON/JSONB.</summary>
    public string ResultJson { get; set; } = "{}";

    public double? Confidence { get; set; }

    public string? PromptVersion { get; set; }

    public string? ModelName { get; set; }
}
