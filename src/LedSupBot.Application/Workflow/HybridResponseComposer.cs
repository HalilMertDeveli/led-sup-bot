using System.Text.RegularExpressions;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Application.Workflow.Parsing;
using LedSupBot.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace LedSupBot.Application.Workflow;

public sealed record HybridResponseComposeInput(
    ConversationContext Context,
    WorkflowSlotState Slots,
    ConversationStage EffectiveStage,
    WorkflowInboundMessage Inbound,
    IReadOnlyList<string> DeterministicClarifications,
    ValidatedAiResponsePlan? AiPlan,
    bool ConfigExampleAssetAvailable,
    bool ConfigExampleCompleted,
    string? ConfigExampleImagePath,
    ConversationResponseAction ResponseAction = ConversationResponseAction.WorkflowPrompt,
    PendingWorkflowFact PendingFact = PendingWorkflowFact.None,
    LedSupBot.Application.Abstractions.ConversationIntent CanonicalIntent = LedSupBot.Application.Abstractions.ConversationIntent.Unclear);

public sealed record HybridResponseComposeResult(
    IReadOnlyList<string> OutboundTexts,
    string? ConfigExampleImagePath,
    bool ConfigExampleMarkedSent);

public sealed class HybridResponseComposer
{
    private readonly ILogger<HybridResponseComposer> _logger;

    public HybridResponseComposer(ILogger<HybridResponseComposer> logger) => _logger = logger;

    public HybridResponseComposeResult Compose(HybridResponseComposeInput input)
    {
        var outbound = new List<string>();
        string? configImagePath = null;
        var configExampleMarkedSent = false;

        if (input.ResponseAction == ConversationResponseAction.AnswerAndHoldWorkflow)
        {
            var exactEvidenceUnavailable = input.Context.KnowledgeGrounding?.ExactClaimNeedsModel == true &&
                input.Context.WebGrounding?.HasOfficialSource != true;
            outbound.Add(input.Context.KnowledgeGrounding?.ConflictDetected == true
                ? "Bu model için dokümanlarda farklı bilgi görüyorum. Cihazın tam model ve versiyonunu netleştirelim; etiketini paylaşabilir misiniz?"
                : exactEvidenceUnavailable
                ? input.Context.KnowledgeGrounding?.DetectedModels.Count > 0
                    ? "Bu model için doğrulanmış kesin teknik değeri mevcut kaynaklarda bulamadım. Resmî doküman veya ürün etiketini paylaşırsanız netleştirebilirim."
                    : "Net teknik değer söylemek için cihazın tam model ve versiyon etiketini görmem lazım. Etiketini veya fotoğrafını atabilir misiniz?"
                : input.AiPlan?.HasUsableReply == true
                ? input.AiPlan.Reply!
                : "Bu kısmı netleştirelim. Sorun hangi noktadan sonra başlıyor?");
            return new HybridResponseComposeResult(outbound, null, false);
        }

        if (input.ResponseAction == ConversationResponseAction.ClarifyCurrentStep)
        {
            outbound.Add(ContextualClarificationResolver.Resolve(input));
            if (input.PendingFact == PendingWorkflowFact.ConfigConnection && ShouldAttachConfigImage(input, ""))
            {
                configExampleMarkedSent = TryAppendConfigExampleStep(input, outbound, ref configImagePath);
            }
            return new HybridResponseComposeResult(outbound, configImagePath, configExampleMarkedSent);
        }

        if (input.ResponseAction == ConversationResponseAction.ResumeWorkflow)
        {
            AppendDeterministicStageFallback(input, outbound, ref configImagePath, ref configExampleMarkedSent);
            return new HybridResponseComposeResult(outbound, configImagePath, configExampleMarkedSent);
        }

        if (input.ResponseAction == ConversationResponseAction.RecoveryResponse)
        {
            outbound.Add(BuildRecoveryReply(input));
            return new HybridResponseComposeResult(outbound, null, false);
        }

        if (input.ResponseAction == ConversationResponseAction.AnswerOnly &&
            input.CanonicalIntent == LedSupBot.Application.Abstractions.ConversationIntent.Acknowledgement &&
            input.Context.CompletedCasesCount > 0 && string.IsNullOrWhiteSpace(input.Context.CurrentIssue))
        {
            outbound.Add("Rica ederim, iyi çalışmalar. Bir durum olursa buradayım.");
            return new HybridResponseComposeResult(outbound, null, false);
        }

        // Explicit answer/clarification actions hold the deterministic workflow for this turn.
        if (input.AiPlan?.HasUsableReply == true &&
            !(input.ResponseAction == ConversationResponseAction.WorkflowPrompt && input.AiPlan.IsWorkflowInformation))
        {
            var reply = input.AiPlan.Reply!;
            outbound.Add(reply);

            // If customer asked about config cable or stage requires config example image
            if (ShouldAttachConfigImage(input, reply))
            {
                configExampleMarkedSent = TryAppendConfigExampleStep(input, outbound, ref configImagePath);
            }

            return new HybridResponseComposeResult(outbound, configImagePath, configExampleMarkedSent);
        }

        if (input.ResponseAction == ConversationResponseAction.AnswerOnly)
        {
            var inboundText = input.Inbound.TextContent ?? string.Empty;
            outbound.Add(ConversationSemantics.IsWaitAcknowledgement(inboundText)
                ? "Tamam, bekliyorum."
                : ConversationSemantics.IsPhotoOffer(inboundText)
                    ? "Evet, etiketi net görecek şekilde gönderin."
                : input.Context.CompletedCasesCount > 0
                ? "Tabii, sorunuzu yazabilirsiniz."
                : "Tamam.");
            return new HybridResponseComposeResult(outbound, null, false);
        }

        // 2. Deterministic clarifications (e.g. meter confirmation, specific partial prompt)
        if (input.DeterministicClarifications.Count > 0)
        {
            var filteredClarifications = input.DeterministicClarifications
                .Where(c => !IsClarificationRedundantWithKnownFacts(c, input.Slots))
                .ToList();

            if (filteredClarifications.Count > 0)
            {
                outbound.AddRange(filteredClarifications);
                if (filteredClarifications.Any(c => c == WorkflowPrompts.ExplainConfigConnection || c == WorkflowPrompts.ConfigExampleSent))
                {
                    configExampleMarkedSent = TryAppendConfigExampleStep(input, outbound, ref configImagePath);
                }
                return new HybridResponseComposeResult(outbound, configImagePath, configExampleMarkedSent);
            }
        }

        // 3. Greeting fallback
        if (ShouldUseGreetingFallback(input))
        {
            outbound.Add(WorkflowPrompts.NaturalGreetingNoIssue);
            return new HybridResponseComposeResult(outbound, null, false);
        }

        // 4. Deterministic stage fallback with anti-looping protection
        AppendDeterministicStageFallback(input, outbound, ref configImagePath, ref configExampleMarkedSent);

        // Apply anti-repetition guard to prevent sending identical prompts back-to-back
        ApplyAntiRepetitionGuard(input, outbound);

        return new HybridResponseComposeResult(outbound, configImagePath, configExampleMarkedSent);
    }

    private static string BuildRecoveryReply(HybridResponseComposeInput input)
    {
        var inbound = input.Inbound.TextContent ?? string.Empty;
        var normalized = ConversationSemantics.Normalize(inbound);
        if (normalized.Contains("ne alaka", StringComparison.Ordinal) ||
            normalized.Contains("onu sormad", StringComparison.Ordinal) ||
            normalized.Contains("onu kastetmed", StringComparison.Ordinal) ||
            normalized.Contains("oyle demed", StringComparison.Ordinal))
        {
            return "Siz neyi soruyordunuz?";
        }

        if (normalized.Contains("yanlis anlad", StringComparison.Ordinal))
        {
            return "Tam olarak konuyu özetler misiniz?";
        }

        if (normalized.Contains("baska bir sey", StringComparison.Ordinal))
        {
            return "Tamam, önceki konuya dönelim. Tam olarak neyi soruyordunuz?";
        }

        if ((normalized.Contains("niye tekrar", StringComparison.Ordinal) ||
             normalized.Contains("ayni seyi", StringComparison.Ordinal) ||
             normalized.Contains("kac kere", StringComparison.Ordinal) ||
             normalized.Contains("kac defa", StringComparison.Ordinal) ||
             normalized.Contains("beni anlamiyorsun", StringComparison.Ordinal)) &&
            !input.Slots.IsFactKnown(input.PendingFact))
        {
            return "Tam olarak netleşmediği için tekrar sordum.";
        }

        var prefix = input.PendingFact == PendingWorkflowFact.ScreenDimensions && input.Slots.IsScreenComplete == false
            ? "Ekran ölçüsü kayıtlarımızda tamamlanmış görünmüyor. Yanlış yönlendirmeyeyim; "
            : input.Slots.IsScreenComplete
                ? "Doğru, ekran ölçüsü tamam. "
                : "Haklısınız, kayıtlı bilgilerden devam edelim. ";
        return prefix + (input.PendingFact switch
        {
            PendingWorkflowFact.ScreenDimensions => "Ekranın yatay ve dikey ölçüsü kaç cm?",
            PendingWorkflowFact.PanelLabel => "Panel arkasındaki etiketi fotoğraf olarak gönderebilir veya P2.5+1065S+2012 şeklinde yazabilirsiniz.",
            PendingWorkflowFact.ReceiverCount => "Receiver tarafında kalmıştık. Kaç tane var?",
            PendingWorkflowFact.ReceiverModel => "Receiver adedi tamam. Kartın modeli nedir?",
            PendingWorkflowFact.PlayerType => "Receiver bilgileri tamam. Oynatıcı Playbox mı, processor mü?",
            PendingWorkflowFact.ProcessorHdmi => "Processor tarafında kalmıştık. HDMI bağlantısı yapıldı mı?",
            PendingWorkflowFact.ConfigConnection => "Config bağlantısında kalmıştık. USB config kablosu bağlı mı?",
            PendingWorkflowFact.AnyDesk => "AnyDesk adresinde kalmıştık; adresi paylaşabilir misiniz?",
            _ => "Kaldığımız adımdan devam edelim."
        });
    }

    private static bool ShouldAttachConfigImage(HybridResponseComposeInput input, string reply)
    {
        if (input.Slots.ConfigConfirmed)
        {
            return false;
        }

        var lowerInbound = input.Inbound.TextContent?.ToLower(new System.Globalization.CultureInfo("tr-TR")) ?? "";

        // Explicit request to send or resend image
        var isExplicitImageRequest = Regex.IsMatch(lowerInbound, @"\b(?:fotoğraf|fotograf|resim|görsel|gorsel|kabloyu\s+göster|kabloyu\s+goster|foto\s+at)\b");
        var isResendRequest = Regex.IsMatch(lowerInbound, @"\b(?:tekrar|yeniden|gelmedi|göremedim|goremedim)\b");

        if (isExplicitImageRequest || (isResendRequest && (lowerInbound.Contains("kablo") || lowerInbound.Contains("resim") || lowerInbound.Contains("görsel"))))
        {
            return true;
        }

        if (input.CanonicalIntent is LedSupBot.Application.Abstractions.ConversationIntent.CasualConversation
            or LedSupBot.Application.Abstractions.ConversationIntent.GeneralKnowledge
            or LedSupBot.Application.Abstractions.ConversationIntent.LedTechnicalQuestion
            or LedSupBot.Application.Abstractions.ConversationIntent.LedTroubleshooting ||
            input.ResponseAction == ConversationResponseAction.AnswerOnly)
        {
            return false;
        }

        if (input.EffectiveStage is ConversationStage.AskConfigConnection or ConversationStage.SendConfigExample && !input.ConfigExampleCompleted)
        {
            return true;
        }

        if (input.PendingFact == PendingWorkflowFact.ConfigConnection && !input.ConfigExampleCompleted)
        {
            return true;
        }

        return false;
    }

    private static bool IsClarificationRedundantWithKnownFacts(string clarification, WorkflowSlotState slots)
    {
        if (slots.IsScreenComplete &&
            (clarification == WorkflowPrompts.AskScreenSize ||
             clarification == WorkflowPrompts.AskScreenSizeAttempt2 ||
             clarification == WorkflowPrompts.AskScreenClarification ||
             clarification == WorkflowPrompts.AskMissingWidth ||
             clarification == WorkflowPrompts.AskMissingHeight ||
             clarification == WorkflowPrompts.AskMeterConfirmation))
        {
            return true;
        }

        if (slots.IsReceiverCountKnown &&
            (clarification == WorkflowPrompts.AskReceiverCount ||
             clarification == WorkflowPrompts.AskReceiverCountAttempt2 ||
             clarification == WorkflowPrompts.AskReceiverClarification))
        {
            return true;
        }

        if (slots.IsReceiverModelKnown &&
            (clarification == WorkflowPrompts.AskReceiverModel ||
             clarification == WorkflowPrompts.AskReceiverModelAttempt2))
        {
            return true;
        }

        if (slots.IsPlayerComplete &&
            (clarification == WorkflowPrompts.AskPlayerType ||
             clarification == WorkflowPrompts.AskPlayerTypeAttempt2))
        {
            return true;
        }

        if (slots.IsProcessorHdmiComplete &&
            (clarification == WorkflowPrompts.AskProcessorHdmi ||
             clarification == WorkflowPrompts.AskProcessorHdmiAttempt2 ||
             clarification == WorkflowPrompts.ExplainProcessorHdmi))
        {
            return true;
        }

        if (slots.ConfigConfirmed &&
            (clarification == WorkflowPrompts.AskConfigConnection ||
             clarification == WorkflowPrompts.AskConfigConnectionAttempt2 ||
             clarification == WorkflowPrompts.ExplainConfigConnection))
        {
            return true;
        }

        if (slots.HasAnyDesk &&
            (clarification == WorkflowPrompts.AskAnyDesk ||
             clarification == WorkflowPrompts.AskAnyDeskAttempt2 ||
             clarification == WorkflowPrompts.AskAnyDeskClarification))
        {
            return true;
        }

        return false;
    }

    private static bool ShouldUseGreetingFallback(HybridResponseComposeInput input)
    {
        if (input.Inbound.MessageType != MessageType.Text ||
            string.IsNullOrWhiteSpace(input.Inbound.TextContent))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(input.Context.CurrentIssue))
        {
            return false;
        }

        if (TechnicalContentHeuristic.MightContainTechnicalInfo(input.Inbound.TextContent))
        {
            return false;
        }

        return GreetingHeuristic.IsGreeting(input.Inbound.TextContent);
    }

    private bool TryAppendConfigExampleStep(
        HybridResponseComposeInput input,
        List<string> outbound,
        ref string? configImagePath)
    {
        if (input.Slots.ConfigConfirmed)
        {
            return false;
        }

        var resolvedPath = ConfigAssetResolver.ResolvePath(input.ConfigExampleImagePath);
        if (!string.IsNullOrWhiteSpace(resolvedPath))
        {
            if (!outbound.Any(t => t.Contains("USB", StringComparison.OrdinalIgnoreCase) || t.Contains("kablo", StringComparison.OrdinalIgnoreCase) || t.Contains("görsel", StringComparison.OrdinalIgnoreCase)))
            {
                outbound.Add(WorkflowPrompts.ConfigExampleSent);
            }

            configImagePath = resolvedPath;
            return true;
        }

        if (input.AiPlan?.HasUsableReply != true && !outbound.Any(t => t.Contains("config", StringComparison.OrdinalIgnoreCase)))
        {
            outbound.Add(WorkflowPrompts.ConfigExampleUnavailable);
            _logger.LogWarning("Config example asset not found on disk; step continuing deterministically");
            return true;
        }

        return false;
    }

    private void AppendDeterministicStageFallback(
        HybridResponseComposeInput input,
        List<string> outbound,
        ref string? configImagePath,
        ref bool configExampleMarkedSent)
    {
        if (outbound.Any(t =>
                t.Contains("teyit", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Yatay", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Dikey", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Örnek:", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        switch (input.EffectiveStage)
        {
            case ConversationStage.AskScreenSize:
                if (!input.Slots.IsScreenComplete && !input.Slots.IsScreenDeferred)
                {
                    if (input.Slots.WidthCm is > 0 && (input.Slots.HeightCm is null or <= 0))
                    {
                        outbound.Add(WorkflowPrompts.AskMissingHeight);
                    }
                    else if (input.Slots.HeightCm is > 0 && (input.Slots.WidthCm is null or <= 0))
                    {
                        outbound.Add(WorkflowPrompts.AskMissingWidth);
                    }
                    else
                    {
                        var attempts = input.Slots.GetAttempts("ScreenDimensions");
                        outbound.Add(attempts >= 1 ? WorkflowPrompts.AskScreenSizeAttempt2 : WorkflowPrompts.AskScreenSize);
                    }
                }
                break;

            case ConversationStage.AskPanelLabelPhotos:
                if (!input.Slots.HasPanelLabelPhoto && !input.Slots.IsPanelPhotoDeferred)
                {
                    var attempts = input.Slots.GetAttempts("PanelLabel");
                    outbound.Add(attempts >= 1
                        ? WorkflowPrompts.AskPanelLabelPhotosAttempt2
                        : (input.Inbound.MessageType == MessageType.Text
                            ? WorkflowPrompts.RemindPanelLabelPhotos
                            : WorkflowPrompts.AskPanelLabelPhotos));
                }
                break;

            case ConversationStage.AskReceiverCount:
                if (!input.Slots.IsReceiverCountKnown && !input.Slots.IsReceiverCountDeferred)
                {
                    var attempts = input.Slots.GetAttempts("ReceiverCount");
                    if (input.Slots.IsReceiverModelKnown)
                    {
                        outbound.Add(WorkflowPrompts.AskReceiverCountForModel(
                            ReceiverModelParser.ToDisplayString(input.Slots.ReceiverModel!.Value)));
                    }
                    else
                    {
                        outbound.Add(attempts >= 1
                            ? WorkflowPrompts.AskReceiverCountAttempt2
                            : WorkflowPrompts.AskReceiverCount);
                    }
                }
                break;

            case ConversationStage.AskReceiverModel:
                if (!input.Slots.IsReceiverModelKnown && !input.Slots.IsReceiverModelDeferred)
                {
                    var attempts = input.Slots.GetAttempts("ReceiverModel");
                    if (input.Slots.IsReceiverCountKnown)
                    {
                        outbound.Add(attempts >= 1
                            ? WorkflowPrompts.AskReceiverModelAttempt2
                            : WorkflowPrompts.AskReceiverModelForCount(input.Slots.ReceiverCount!.Value));
                    }
                    else
                    {
                        outbound.Add(attempts >= 1
                            ? WorkflowPrompts.AskReceiverModelAttempt2
                            : WorkflowPrompts.AskReceiverModel);
                    }
                }
                break;

            case ConversationStage.AskPlayerType:
                if (!input.Slots.IsPlayerComplete && !input.Slots.IsPlayerTypeDeferred)
                {
                    var attempts = input.Slots.GetAttempts("PlayerType");
                    outbound.Add(attempts >= 1 ? WorkflowPrompts.AskPlayerTypeAttempt2 : WorkflowPrompts.AskPlayerType);
                }
                break;

            case ConversationStage.AskProcessorHdmi:
                if (!input.Slots.IsProcessorHdmiComplete && !input.Slots.IsProcessorHdmiDeferred)
                {
                    var attempts = input.Slots.GetAttempts("ProcessorHdmi");
                    outbound.Add(attempts >= 1 ? WorkflowPrompts.AskProcessorHdmiAttempt2 : WorkflowPrompts.AskProcessorHdmi);
                }
                break;

            case ConversationStage.AskConfigConnection:
                if (!input.Slots.ConfigConfirmed && !input.Slots.IsConfigConnectionDeferred)
                {
                    var attempts = input.Slots.GetAttempts("ConfigConnection");
                    if (attempts == 0)
                    {
                        outbound.Add(WorkflowPrompts.AskConfigConnection);
                        configExampleMarkedSent = TryAppendConfigExampleStep(input, outbound, ref configImagePath);
                    }
                    else
                    {
                        outbound.Add(WorkflowPrompts.AskConfigConnectionAttempt2);
                    }
                }
                break;

            case ConversationStage.AskAnyDesk:
                if (!input.Slots.HasAnyDesk && !input.Slots.IsAnyDeskDeferred)
                {
                    var attempts = input.Slots.GetAttempts("AnyDesk");
                    outbound.Add(attempts >= 1 ? WorkflowPrompts.AskAnyDeskAttempt2 : WorkflowPrompts.AskAnyDesk);
                }
                break;
        }
    }

    private static void ApplyAntiRepetitionGuard(HybridResponseComposeInput input, List<string> outbound)
    {
        if (outbound.Count == 0 || input.Context.RecentMessages.Count == 0)
        {
            return;
        }

        var lastBotMessage = input.Context.RecentMessages
            .Where(m => m.Role is "bot" or "technician")
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => m.Content)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(lastBotMessage))
        {
            return;
        }

        for (int i = 0; i < outbound.Count; i++)
        {
            if (string.Equals(outbound[i].Trim(), lastBotMessage.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                outbound[i] = RephrasePrompt(outbound[i]);
            }
        }
    }

    private static string RephrasePrompt(string originalPrompt)
    {
        if (originalPrompt == WorkflowPrompts.AskScreenSize || originalPrompt == WorkflowPrompts.AskScreenClarification)
        {
            return WorkflowPrompts.AskScreenSizeAttempt2;
        }

        if (originalPrompt == WorkflowPrompts.AskReceiverCount || originalPrompt == WorkflowPrompts.AskReceiverClarification)
        {
            return WorkflowPrompts.AskReceiverCountAttempt2;
        }

        if (originalPrompt == WorkflowPrompts.AskReceiverModel)
        {
            return WorkflowPrompts.AskReceiverModelAttempt2;
        }

        if (originalPrompt == WorkflowPrompts.AskPlayerType)
        {
            return WorkflowPrompts.AskPlayerTypeAttempt2;
        }

        if (originalPrompt == WorkflowPrompts.AskProcessorHdmi)
        {
            return WorkflowPrompts.AskProcessorHdmiAttempt2;
        }

        if (originalPrompt == WorkflowPrompts.AskConfigConnection || originalPrompt == WorkflowPrompts.ExplainConfigConnection)
        {
            return WorkflowPrompts.AskConfigConnectionAttempt2;
        }

        if (originalPrompt == WorkflowPrompts.AskAnyDesk || originalPrompt == WorkflowPrompts.AskAnyDeskClarification)
        {
            return WorkflowPrompts.AskAnyDeskAttempt2;
        }

        return originalPrompt;
    }
}
