using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Admin;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LedSupBot.Application.Workflow.Context;

public sealed class ConversationContextBuilder : IConversationContextBuilder
{
    private const int MaxRecentMessages = 10;

    private readonly IApplicationDbContext _db;

    public ConversationContextBuilder(IApplicationDbContext db) => _db = db;

    public async Task<ConversationContext> BuildAsync(
        Conversation conversation,
        TechnicalSupportCase supportCase,
        WorkflowInboundMessage inbound,
        WorkflowSlotState slots,
        ConversationStage effectiveStage,
        CancellationToken cancellationToken = default)
    {
        var messages = await _db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id)
            .OrderByDescending(m => m.CreatedAt)
            .Take(MaxRecentMessages)
            .ToListAsync(cancellationToken);

        messages.Reverse();

        var recent = messages
            .Select(m => new ContextMessage(
                MapRole(m.SentBy, m.Direction),
                TrimContent(m),
                m.CreatedAt))
            .ToList();

        var known = BuildKnownFacts(slots, supportCase);
        var missing = BuildMissingFacts(slots, effectiveStage);

        return new ConversationContext
        {
            ConversationId = conversation.Id,
            CurrentStage = effectiveStage,
            StageLabel = AdminStageLabels.ToLabel(effectiveStage),
            RecentMessages = recent,
            CurrentIssue = supportCase.IssueSummary,
            KnownFacts = known,
            MissingFacts = missing,
            Screen = new ContextScreen(
                slots.IsScreenComplete ? $"{slots.WidthCm:0} × {slots.HeightCm:0} cm" : null,
                slots.IsScreenComplete),
            Receiver = new ContextReceiver(
                BuildReceiverText(supportCase.ReceiverInformation),
                slots.ReceiverCount is > 0),
            Panel = new ContextPanel(
                slots.HasPanelLabelPhoto ? "Panel etiketi alındı" : "Panel etiketi bekleniyor",
                slots.HasPanelLabelPhoto),
            Config = new ContextConfig(
                slots.ConfigConfirmed ? "Config bağlantısı tamam" : "Config bağlantısı bekleniyor",
                slots.ConfigConfirmed),
            AnyDesk = new ContextAnyDesk(
                slots.HasAnyDesk ? "AnyDesk alındı" : "AnyDesk bekleniyor",
                slots.HasAnyDesk),
            BotMode = conversation.BotMode,
        };
    }

    private static string MapRole(MessageSender sentBy, MessageDirection direction) =>
        sentBy switch
        {
            MessageSender.Customer => "customer",
            MessageSender.Bot => "bot",
            MessageSender.Technician => "technician",
            MessageSender.System => "system",
            _ => direction == MessageDirection.Inbound ? "customer" : "bot",
        };

    private static string TrimContent(Message message)
    {
        if (message.MessageType == MessageType.Image)
        {
            return string.IsNullOrWhiteSpace(message.Content) ? "[fotoğraf]" : message.Content;
        }

        var text = message.Content?.Trim() ?? string.Empty;
        return text.Length <= 400 ? text : text[..400] + "…";
    }

    private static List<string> BuildKnownFacts(WorkflowSlotState slots, TechnicalSupportCase supportCase)
    {
        var facts = new List<string>();
        if (slots.IsScreenComplete)
        {
            facts.Add($"Ekran: {slots.WidthCm:0}×{slots.HeightCm:0} cm");
        }

        if (slots.HasPanelLabelPhoto)
        {
            facts.Add("Panel etiket fotoğrafı alındı");
        }

        if (slots.ReceiverCount is > 0)
        {
            facts.Add($"Receiver: {BuildReceiverText(supportCase.ReceiverInformation)}");
        }

        if (slots.ConfigConfirmed)
        {
            facts.Add("Config bağlantısı onaylandı");
        }

        if (slots.HasAnyDesk)
        {
            facts.Add("AnyDesk bilgisi alındı");
        }

        if (!string.IsNullOrWhiteSpace(supportCase.IssueSummary))
        {
            facts.Add($"Sorun: {supportCase.IssueSummary}");
        }

        return facts;
    }

    private static List<string> BuildMissingFacts(WorkflowSlotState slots, ConversationStage stage)
    {
        var missing = new List<string>();
        if (!slots.IsScreenComplete)
        {
            missing.Add("Ekran ölçüsü");
        }

        if (!slots.HasPanelLabelPhoto)
        {
            missing.Add("Panel etiket fotoğrafı");
        }

        if (slots.ReceiverCount is null or <= 0)
        {
            missing.Add("Receiver adedi/marka");
        }

        if (!slots.ConfigConfirmed)
        {
            missing.Add("Config bağlantı durumu");
        }

        if (!slots.HasAnyDesk && stage is ConversationStage.AskAnyDesk or ConversationStage.CollectingFinalInfo)
        {
            missing.Add("AnyDesk adresi");
        }

        return missing;
    }

    private static string? BuildReceiverText(ReceiverInformation? receiver)
    {
        if (receiver is null || receiver.Count <= 0)
        {
            return null;
        }

        var parts = new List<string> { $"×{receiver.Count}" };
        if (!string.IsNullOrWhiteSpace(receiver.Manufacturer))
        {
            parts.Insert(0, receiver.Manufacturer);
        }

        if (!string.IsNullOrWhiteSpace(receiver.Model))
        {
            parts.Add(receiver.Model);
        }

        return string.Join(" ", parts);
    }
}
