using System.Globalization;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Admin;
using LedSupBot.Application.Workflow.Parsing;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LedSupBot.Application.Workflow.Context;

public sealed class ConversationContextBuilder : IConversationContextBuilder
{
    private const int MaxRecentMessages = 10;

    private readonly IApplicationDbContext _db;

    public ConversationContextBuilder(IApplicationDbContext db)
    {
        _db = db;
    }

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

        var lastBotQuestion = recent
            .Where(m => m.Role is "bot" or "technician")
            .Where(m => IsRelevantBotQuestion(m.Content))
            .Select(m => m.Content)
            .LastOrDefault();

        var completedCases = await _db.TechnicalSupportCases
            .AsNoTracking()
            .Where(tc => tc.ConversationId == conversation.Id && tc.Status == SupportCaseStatus.Completed)
            .OrderByDescending(tc => tc.CompletedAt ?? tc.CreatedAt)
            .Take(5)
            .ToListAsync(cancellationToken);

        var previousCasesSummary = completedCases.Count > 0
            ? string.Join("; ", completedCases.Select(c => $"[Tamamlandı {c.CompletedAt:dd.MM.yyyy}: {c.IssueSummary ?? c.SummaryMarkdown ?? "LED Destek"}]"))
            : null;

        var known = BuildKnownFacts(slots, supportCase, completedCases.Count);
        var missing = BuildMissingFacts(slots, effectiveStage);
        var askableMissing = BuildAskableMissingFacts(slots, effectiveStage);
        var deferredMissing = BuildDeferredMissingFacts(slots, effectiveStage);

        return new ConversationContext
        {
            ConversationId = conversation.Id,
            CurrentStage = effectiveStage,
            StageLabel = AdminStageLabels.ToLabel(effectiveStage),
            RecentMessages = recent,
            LastBotQuestion = lastBotQuestion,
            PendingFact = WorkflowStageResolver.ResolvePendingFact(effectiveStage),
            CurrentIssue = supportCase.IssueSummary,
            KnownFacts = known,
            MissingFacts = askableMissing,
            AskableMissingFacts = askableMissing,
            DeferredMissingFacts = deferredMissing,
            Screen = new ContextScreen(
                slots.IsScreenComplete ? $"{slots.WidthCm:0} × {slots.HeightCm:0} cm" : (slots.IsScreenDeferred ? "Bilgi alınamadı" : null),
                slots.IsScreenComplete,
                slots.IsScreenDeferred),
            Receiver = new ContextReceiver(
                BuildReceiverText(supportCase.ReceiverInformation, slots),
                slots.ReceiverCount is > 0,
                slots.ReceiverCount is > 0,
                slots.IsReceiverModelKnown,
                slots.ReceiverCount,
                slots.ReceiverModel,
                slots.IsReceiverCountDeferred,
                slots.IsReceiverModelDeferred),
            Player = new ContextPlayer(
                slots.PlayerType is not null ? PlayerTypeParser.ToDisplayString(slots.PlayerType.Value) : (slots.IsPlayerTypeDeferred ? "Bilgi alınamadı" : null),
                slots.IsPlayerComplete,
                slots.PlayerType,
                slots.IsPlayerTypeDeferred),
            ProcessorHdmi = new ContextProcessorHdmi(
                slots.ProcessorHdmiConnected == true ? "HDMI bağlı" : (slots.IsProcessorHdmiDeferred ? "Bilgi alınamadı" : (slots.PlayerType == PlayerType.Processor ? "HDMI bekleniyor" : null)),
                slots.IsProcessorHdmiComplete,
                slots.ProcessorHdmiConnected,
                slots.IsProcessorHdmiDeferred),
            Panel = new ContextPanel(
                slots.HasPanelLabelPhoto ? "Panel etiketi alındı" : (slots.IsPanelPhotoDeferred ? "Bilgi alınamadı" : "Panel etiketi bekleniyor"),
                slots.HasPanelLabelPhoto,
                slots.IsPanelPhotoDeferred),
            Config = new ContextConfig(
                slots.ConfigConfirmed ? "Config bağlantısı tamam" : (slots.IsConfigConnectionDeferred ? "Bilgi alınamadı" : "Config bağlantısı bekleniyor"),
                slots.ConfigConfirmed,
                slots.IsConfigConnectionDeferred),
            AnyDesk = new ContextAnyDesk(
                slots.HasAnyDesk ? "AnyDesk alındı" : (slots.IsAnyDeskDeferred ? "Bilgi alınamadı" : "AnyDesk bekleniyor"),
                slots.HasAnyDesk,
                slots.IsAnyDeskDeferred),
            BotMode = conversation.BotMode,
            CurrentCaseStatus = supportCase.Status,
            CompletedCasesCount = completedCases.Count,
            PreviousCasesSummary = previousCasesSummary,
            AiOperatorSummary = supportCase.AiOperatorSummary,
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

    private static bool IsRelevantBotQuestion(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        if (content.Contains('?')) return true;
        var normalized = content.ToLower(new CultureInfo("tr-TR"));
        return normalized.Contains("paylaş") || normalized.Contains("gönder") ||
               normalized.Contains("belirt") || normalized.Contains("kontrol ed") ||
               normalized.Contains("bağlay") || normalized.Contains("takın");
    }

    private static string TrimContent(Message message)
    {
        if (message.MessageType == MessageType.Image)
        {
            return string.IsNullOrWhiteSpace(message.Content) ? "[fotoğraf]" : message.Content;
        }

        var text = message.Content?.Trim() ?? string.Empty;
        return text.Length <= 400 ? text : text[..400] + "…";
    }

    private static List<string> BuildKnownFacts(WorkflowSlotState slots, TechnicalSupportCase supportCase, int completedCasesCount)
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
            var receiverText = slots.IsReceiverModelKnown
                ? $"{slots.ReceiverCount} adet {ReceiverModelParser.ToDisplayString(slots.ReceiverModel!.Value)}"
                : $"{slots.ReceiverCount} adet";
            facts.Add($"Receiver: {receiverText}");
        }
        else if (slots.IsReceiverModelKnown)
        {
            facts.Add($"Receiver modeli: {ReceiverModelParser.ToDisplayString(slots.ReceiverModel!.Value)}");
        }

        if (slots.IsPlayerComplete)
        {
            facts.Add($"Oynatıcı türü: {PlayerTypeParser.ToDisplayString(slots.PlayerType!.Value)}");
        }

        if (slots.PlayerType == PlayerType.Processor && slots.ProcessorHdmiConnected == true)
        {
            facts.Add("Processor HDMI bağlantısı yapıldı");
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

        if (facts.Count == 0 && completedCasesCount > 0)
        {
            facts.Add($"Önceki {completedCasesCount} adet teknik destek kaydı başarıyla tamamlandı.");
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

        if (!slots.IsReceiverCountKnown)
        {
            missing.Add("Receiver adedi");
        }

        if (!slots.IsReceiverModelKnown)
        {
            missing.Add("Receiver modeli");
        }

        if (!slots.IsPlayerComplete)
        {
            missing.Add("Oynatıcı türü");
        }

        if (slots.PlayerType == PlayerType.Processor && !slots.IsProcessorHdmiComplete)
        {
            missing.Add("Processor HDMI bağlantısı");
        }

        if (!slots.ConfigConfirmed)
        {
            missing.Add("Config bağlantı durumu");
        }

        if (!slots.HasAnyDesk)
        {
            missing.Add("AnyDesk adresi");
        }

        return missing;
    }

    private static List<string> BuildAskableMissingFacts(WorkflowSlotState slots, ConversationStage stage)
    {
        var askable = new List<string>();
        if (!slots.IsScreenComplete && !slots.IsScreenDeferred)
        {
            askable.Add("Ekran ölçüsü");
        }

        if (!slots.HasPanelLabelPhoto && !slots.IsPanelPhotoDeferred)
        {
            askable.Add("Panel etiket fotoğrafı");
        }

        if (!slots.IsReceiverCountKnown && !slots.IsReceiverCountDeferred)
        {
            askable.Add("Receiver adedi");
        }

        if (!slots.IsReceiverModelKnown && !slots.IsReceiverModelDeferred)
        {
            askable.Add("Receiver modeli");
        }

        if (!slots.IsPlayerComplete && !slots.IsPlayerTypeDeferred)
        {
            askable.Add("Oynatıcı türü");
        }

        if (slots.PlayerType == PlayerType.Processor && !slots.IsProcessorHdmiComplete && !slots.IsProcessorHdmiDeferred)
        {
            askable.Add("Processor HDMI bağlantısı");
        }

        if (!slots.ConfigConfirmed && !slots.IsConfigConnectionDeferred)
        {
            askable.Add("Config bağlantı durumu");
        }

        if (!slots.HasAnyDesk && !slots.IsAnyDeskDeferred)
        {
            askable.Add("AnyDesk adresi");
        }

        return askable;
    }

    private static List<string> BuildDeferredMissingFacts(WorkflowSlotState slots, ConversationStage stage)
    {
        var deferred = new List<string>();
        if (!slots.IsScreenComplete && slots.IsScreenDeferred)
        {
            deferred.Add("Ekran ölçüsü (2 kez soruldu, atlandı)");
        }

        if (!slots.HasPanelLabelPhoto && slots.IsPanelPhotoDeferred)
        {
            deferred.Add("Panel etiket fotoğrafı (2 kez soruldu, atlandı)");
        }

        if (!slots.IsReceiverCountKnown && slots.IsReceiverCountDeferred)
        {
            deferred.Add("Receiver adedi (2 kez soruldu, atlandı)");
        }

        if (!slots.IsReceiverModelKnown && slots.IsReceiverModelDeferred)
        {
            deferred.Add("Receiver modeli (2 kez soruldu, atlandı)");
        }

        if (!slots.IsPlayerComplete && slots.IsPlayerTypeDeferred)
        {
            deferred.Add("Oynatıcı türü (2 kez soruldu, atlandı)");
        }

        if (slots.PlayerType == PlayerType.Processor && !slots.IsProcessorHdmiComplete && slots.IsProcessorHdmiDeferred)
        {
            deferred.Add("Processor HDMI bağlantısı (2 kez soruldu, atlandı)");
        }

        if (!slots.ConfigConfirmed && slots.IsConfigConnectionDeferred)
        {
            deferred.Add("Config bağlantı durumu (2 kez soruldu, atlandı)");
        }

        if (!slots.HasAnyDesk && slots.IsAnyDeskDeferred)
        {
            deferred.Add("AnyDesk adresi (2 kez soruldu, atlandı)");
        }

        return deferred;
    }

    private static string? BuildReceiverText(ReceiverInformation? receiver, WorkflowSlotState slots)
    {
        if (slots.ReceiverCount is null or <= 0)
        {
            if (slots.IsReceiverModelKnown)
            {
                return ReceiverModelParser.ToDisplayString(slots.ReceiverModel!.Value);
            }
            return slots.IsReceiverCountDeferred ? "Bilgi alınamadı" : null;
        }

        if (slots.IsReceiverModelKnown)
        {
            return $"{slots.ReceiverCount} adet {ReceiverModelParser.ToDisplayString(slots.ReceiverModel!.Value)}";
        }

        if (receiver is not null && !string.IsNullOrWhiteSpace(receiver.Model))
        {
            return $"{slots.ReceiverCount} adet {receiver.Model}";
        }

        return $"{slots.ReceiverCount} adet";
    }
}
