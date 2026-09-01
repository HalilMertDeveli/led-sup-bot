using LedSupBot.Application.Abstractions;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LedSupBot.Application.Workflow.Ai;

public sealed class AiExtractionAuditWriter
{
    private readonly IApplicationDbContext _db;

    public AiExtractionAuditWriter(IApplicationDbContext db) => _db = db;

    public async Task WriteAsync(
        Guid conversationId,
        Guid messageId,
        ValidatedAiExtraction validated,
        string? modelName,
        CancellationToken cancellationToken)
    {
        var slotsFilled = CountSlotsFilled(validated);
        var summary = JsonSerializer.Serialize(new
        {
            intent = validated.Intent.ToString(),
            slotsFilled,
            avgConfidence = validated.AverageConfidence,
        });

        _db.AiExtractionResults.Add(new AiExtractionResult
        {
            ConversationId = conversationId,
            MessageId = messageId,
            ExtractionType = AiExtractionType.SlotExtraction,
            ResultJson = summary,
            Confidence = validated.AverageConfidence,
            PromptVersion = OpenAiPromptVersion.Current,
            ModelName = modelName,
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static int CountSlotsFilled(ValidatedAiExtraction validated)
    {
        var count = 0;
        if (validated.Screen?.Complete == true)
        {
            count++;
        }

        if (validated.ReceiverCount is > 0)
        {
            count++;
        }

        if (validated.ConfigConfirmed == true)
        {
            count++;
        }

        if (!string.IsNullOrWhiteSpace(validated.AnyDeskAddress))
        {
            count++;
        }

        return count;
    }
}

public static class OpenAiPromptVersion
{
    public const string Current = "phase6-v1";
}
