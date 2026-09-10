using System.Text.RegularExpressions;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow.Context;

namespace LedSupBot.Application.Workflow.Ai;

public interface IAiConversationPlanner
{
    Task<AiResponsePlanRaw?> TryPlanAsync(
        ConversationContext context,
        string latestCustomerMessage,
        CancellationToken cancellationToken = default);
}

public sealed record AiResponsePlanRaw(
    string Intent,
    string? Reply,
    string? IssueSummary,
    bool NeedsHuman,
    string? SuggestedNextAction,
    string? AiOperatorSummary = null,
    string? CustomerQuestion = null,
    bool? ShouldAnswerQuestion = null,
    bool? ShouldContinueWorkflowNow = null,
    bool? ShouldStartNewCase = null,
    bool? NeedsClarification = null,
    string? SuggestedClarification = null,
    double? AnswerConfidence = null,
    string? DiagnosticHypothesis = null);

public sealed record ValidatedAiResponsePlan(
    ConversationIntent Intent,
    string? Reply,
    string? IssueSummary,
    bool NeedsHuman,
    string? SuggestedNextAction,
    string? AiOperatorSummary = null,
    string? CustomerQuestion = null,
    bool? ShouldAnswerQuestion = null,
    bool? ShouldContinueWorkflowNow = null,
    bool? ShouldStartNewCase = null,
    bool? NeedsClarification = null,
    string? SuggestedClarification = null,
    double? AnswerConfidence = null,
    string? DiagnosticHypothesis = null)
{
    public bool HasUsableReply => !string.IsNullOrWhiteSpace(Reply);

    public bool IsGeneralTechnicalQuestion => Intent is ConversationIntent.LedTechnicalQuestion or ConversationIntent.LedTroubleshooting or ConversationIntent.LedTechnicalSupport;

    public bool IsStepClarification =>
        Intent == ConversationIntent.QuestionAboutCurrentStep;

    public bool IsWorkflowInformation =>
        Intent is ConversationIntent.WorkflowInformation or ConversationIntent.Correction;

    public bool IsCasualOrAcknowledgement =>
        Intent is ConversationIntent.Acknowledgement or ConversationIntent.CasualConversation;

    public bool IsNewCaseRequested =>
        ShouldStartNewCase == true ||
        Intent == ConversationIntent.NewCase;
}

public sealed class AiResponsePlanner
{
    private readonly IAiConversationPlanner _planner;

    public AiResponsePlanner(IAiConversationPlanner planner) => _planner = planner;

    public async Task<ValidatedAiResponsePlan?> TryPlanAsync(
        ConversationContext context,
        string latestCustomerMessage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(latestCustomerMessage))
        {
            return null;
        }

        try
        {
            var raw = await _planner.TryPlanAsync(context, latestCustomerMessage, cancellationToken);
            if (raw is null)
            {
                return null;
            }

            var reply = SanitizeReply(raw.Reply, context);
            var issue = SanitizeIssueSummary(raw.IssueSummary, context.CurrentIssue);

            if (string.IsNullOrWhiteSpace(reply) && string.IsNullOrWhiteSpace(issue))
            {
                return null;
            }

            return new ValidatedAiResponsePlan(
                ConversationIntentNormalizer.Normalize(raw.Intent),
                reply,
                issue,
                raw.NeedsHuman,
                raw.SuggestedNextAction,
                raw.AiOperatorSummary,
                raw.CustomerQuestion,
                raw.ShouldAnswerQuestion,
                raw.ShouldContinueWorkflowNow,
                raw.ShouldStartNewCase,
                raw.NeedsClarification,
                raw.SuggestedClarification,
                raw.AnswerConfidence,
                raw.DiagnosticHypothesis);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    public static class ConversationIntentNormalizer
    {
        public static ConversationIntent Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
        {
            "workflow_information" or "technical_support" or "confirmation" or "rejection" => ConversationIntent.WorkflowInformation,
            "customer_question_about_current_step" or "ask_clarification" => ConversationIntent.QuestionAboutCurrentStep,
            "general_technical_question" or "ask_technical_question" => ConversationIntent.LedTechnicalQuestion,
            "led_troubleshooting" or "troubleshooting" => ConversationIntent.LedTroubleshooting,
            "correction" => ConversationIntent.Correction,
            "acknowledgement" or "gratitude" => ConversationIntent.Acknowledgement,
            "flow_recovery" or "conversation_repair" => ConversationIntent.FlowRecovery,
            "return_to_workflow" or "resume_workflow" => ConversationIntent.ResumeWorkflow,
            "casual_conversation" or "humor" or "greeting" => ConversationIntent.CasualConversation,
            "general_knowledge" => ConversationIntent.GeneralKnowledge,
            "new_support_issue" or "new_installation" => ConversationIntent.NewCase,
            "screen_setup" => ConversationIntent.ScreenSetup,
            "file_request" => ConversationIntent.FileRequest,
            "led_technical_support" => ConversationIntent.LedTechnicalSupport,
            _ => ConversationIntent.Unclear,
        };
    }

    private static string? SanitizeReply(string? reply, ConversationContext context)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return null;
        }

        var trimmed = reply.Trim();
        if (trimmed.Length > 1800)
        {
            trimmed = trimmed[..1800];
        }

        if ((context.Screen?.Complete == true || context.Screen?.Deferred == true) && ContainsScreenReAsk(trimmed))
        {
            trimmed = StripScreenReAsk(trimmed);
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return null;
            }
        }

        // Check Receiver Count
        if ((context.Receiver?.Complete == true || context.Receiver?.CountComplete == true || context.Receiver?.CountDeferred == true) && ContainsReceiverCountReAsk(trimmed))
        {
            trimmed = StripReceiverCountReAsk(trimmed);
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return null;
            }
        }

        // Check Receiver Model
        if ((context.Receiver?.Complete == true || context.Receiver?.ModelComplete == true || context.Receiver?.ModelDeferred == true) && ContainsReceiverModelReAsk(trimmed))
        {
            trimmed = StripReceiverModelReAsk(trimmed);
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return null;
            }
        }

        // Check Player Type
        if ((context.Player?.Complete == true || context.Player?.Deferred == true) && ContainsPlayerTypeReAsk(trimmed))
        {
            trimmed = StripPlayerTypeReAsk(trimmed);
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return null;
            }
        }

        // Check Processor HDMI
        if ((context.ProcessorHdmi?.Complete == true || context.ProcessorHdmi?.Deferred == true) && ContainsProcessorHdmiReAsk(trimmed))
        {
            trimmed = StripProcessorHdmiReAsk(trimmed);
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return null;
            }
        }

        if ((context.Panel?.Complete == true || context.Panel?.Deferred == true) && ContainsPanelReAsk(trimmed))
        {
            trimmed = StripPanelReAsk(trimmed);
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return null;
            }
        }

        if ((context.Config?.Complete == true || context.Config?.Deferred == true) && ContainsConfigReAsk(trimmed))
        {
            trimmed = StripConfigReAsk(trimmed);
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return null;
            }
        }

        if ((context.AnyDesk?.Complete == true || context.AnyDesk?.Deferred == true) && ContainsAnyDeskReAsk(trimmed))
        {
            trimmed = StripAnyDeskReAsk(trimmed);
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return null;
            }
        }

        trimmed = SanitizeCorporateAndUnwantedPhrases(trimmed);

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string SanitizeCorporateAndUnwantedPhrases(string reply)
    {
        var patterns = new[]
        {
            @"teknik\s+ekibimiz\s+(?:en\s+kısa\s+sürede\s+)?sizinle\s+iletişime\s+geçecek(?:tir)?\.?",
            @"ekibimiz\s+(?:en\s+kısa\s+sürede\s+)?sizinle\s+iletişime\s+geçecek(?:tir)?\.?",
            @"ekibimiz\s+(?:en\s+kısa\s+sürede\s+)?sizinle\s+irtibata\s+geçecek(?:tir)?\.?",
            @"teknik\s+ekibimiz\s+(?:en\s+kısa\s+sürede\s+)?sizi\s+arayacak(?:tır)?\.?",
            @"sizinle\s+iletişime\s+geçeceğiz\.?",
            @"sizinle\s+irtibata\s+geçeceğiz\.?",
            @"size\s+yardımcı\s+olmaktan\s+memnuniyet\s+duyarım\.?",
            @"memnuniyetle\s+yardımcı\s+olabilirim\.?",
            @"size\s+daha\s+iyi\s+yardımcı\s+olabilmem\s+adına\.?",
            @"harika\s+bir\s+soru\.?",
            @"detaylı\s+bir\s+şekilde\s+açıklayayım\.?"
        };

        var cleaned = reply;
        foreach (var pattern in patterns)
        {
            cleaned = Regex.Replace(
                cleaned,
                pattern,
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return cleaned.Trim();
    }

    private static bool ContainsScreenReAsk(string reply)
    {
        var lower = reply.ToLowerInvariant();
        return Regex.IsMatch(lower, @"\b(?:ekran\s+ölçü|ekran\s+boyut|en\s+ve\s+boy|yatay\s+ve\s+dikey).{0,50}(?:nedir|kaç\s*cm|paylaş|yazın|gönder|belirtin|\?)");
    }

    private static string StripScreenReAsk(string reply) =>
        Regex.Replace(reply, @"[^.!?\n]*\b(?:ekran\s+ölçü|ekran\s+boyut|en\s+ve\s+boy|yatay\s+ve\s+dikey).{0,50}(?:nedir|kaç\s*cm|paylaş|yazın|gönder|belirtin|\?)[^.!?\n]*[.!?]?", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

    private static bool ContainsReceiverCountReAsk(string reply)
    {
        var lower = reply.ToLowerInvariant();
        return Regex.IsMatch(lower, @"\b(?:kaç\s*(?:adet|tane)?\s*(?:receiver|kart)|receiver\s+sayıs|kart\s+sayıs|kaç\s+tane\s+var|kaç\s+adet\s+var).{0,40}(?:nedir|paylaş|yazın|gönder|belirtin|var|\?)");
    }

    private static string StripReceiverCountReAsk(string reply) =>
        Regex.Replace(reply, @"[^.!?\n]*\b(?:kaç\s*(?:adet|tane)?\s*(?:receiver|kart)|receiver\s+sayıs|kart\s+sayıs|kaç\s+tane\s+var|kaç\s+adet\s+var).{0,40}(?:nedir|paylaş|yazın|gönder|belirtin|var|\?)[^.!?\n]*[.!?]?", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

    private static bool ContainsReceiverModelReAsk(string reply)
    {
        var lower = reply.ToLowerInvariant();
        return Regex.IsMatch(lower, @"\b(?:receiver\s+modeli|kart\s+modeli|hangi\s+model|modeli\s+nedir|e120,\s*e80).{0,40}(?:nedir|hangisi|paylaş|yazın|belirtin|\?)");
    }

    private static string StripReceiverModelReAsk(string reply) =>
        Regex.Replace(reply, @"[^.!?\n]*\b(?:receiver\s+modeli|kart\s+modeli|hangi\s+model|modeli\s+nedir|e120,\s*e80).{0,40}(?:nedir|hangisi|paylaş|yazın|belirtin|\?)[^.!?\n]*[.!?]?", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

    private static bool ContainsPlayerTypeReAsk(string reply)
    {
        var lower = reply.ToLowerInvariant();
        return Regex.IsMatch(lower, @"\b(?:playbox|play\s*box).{0,30}(?:mı|mi|mu|mü).{0,30}(?:processor|prosesör)|\boynatıcı\s*(?:olarak|türü).{0,30}(?:playbox|processor)");
    }

    private static string StripPlayerTypeReAsk(string reply) =>
        Regex.Replace(reply, @"[^.!?\n]*\b(?:(?:playbox|play\s*box).{0,30}(?:mı|mi|mu|mü).{0,30}(?:processor|prosesör)|oynatıcı\s*(?:olarak|türü).{0,30}(?:playbox|processor))[^.!?\n]*[.!?]?", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

    private static bool ContainsProcessorHdmiReAsk(string reply)
    {
        var lower = reply.ToLowerInvariant();
        return Regex.IsMatch(lower, @"\bhdmi\s*(?:kablo|bağlantı|çıkış|giriş)?.{0,40}(?:bağlayabildiniz\s*mi|bağladınız\s*mı|taktınız\s*mı|bağlayın|takın|\?)");
    }

    private static string StripProcessorHdmiReAsk(string reply) =>
        Regex.Replace(reply, @"[^.!?\n]*\bhdmi\s*(?:kablo|bağlantı|çıkış|giriş)?.{0,40}(?:bağlayabildiniz\s*mi|bağladınız\s*mı|taktınız\s*mı|bağlayın|takın|\?)[^.!?\n]*[.!?]?", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

    private static bool ContainsPanelReAsk(string reply)
    {
        var lower = reply.ToLowerInvariant();
        return Regex.IsMatch(lower, @"\b(?:etiket|panel).{0,40}(?:fotoğraf|resim).{0,30}(?:gönder|atabilir|paylaş|yükleyin|\?)");
    }

    private static string StripPanelReAsk(string reply) =>
        Regex.Replace(reply, @"[^.!?\n]*\b(?:etiket|panel).{0,40}(?:fotoğraf|resim).{0,30}(?:gönder|atabilir|paylaş|yükleyin|\?)[^.!?\n]*[.!?]?", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

    private static bool ContainsConfigReAsk(string reply)
    {
        var lower = reply.ToLowerInvariant();
        return Regex.IsMatch(lower, @"\b(?:config|yazıcı)\s*kablo.{0,40}(?:bağlayabilir\s*misiniz|bağladınız\s*mı|taktınız\s*mı|bağlayın|takın)");
    }

    private static string StripConfigReAsk(string reply) =>
        Regex.Replace(reply, @"[^.!?\n]*\b(?:config|yazıcı)\s*kablo.{0,40}(?:bağlayabilir\s*misiniz|bağladınız\s*mı|taktınız\s*mı|bağlayın|takın)[^.!?\n]*[.!?]?", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

    private static bool ContainsAnyDeskReAsk(string reply)
    {
        var lower = reply.ToLowerInvariant();
        return Regex.IsMatch(lower, @"\b(?:anydesk).{0,40}(?:adresini(?:z)?|kodunu|numarasını).{0,30}(?:paylaş|gönder|yazın|verebilir|\?)");
    }

    private static string StripAnyDeskReAsk(string reply) =>
        Regex.Replace(reply, @"[^.!?\n]*\b(?:anydesk).{0,40}(?:adresini(?:z)?|kodunu|numarasını).{0,30}(?:paylaş|gönder|yazın|verebilir|\?)[^.!?\n]*[.!?]?", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

    private static string? SanitizeIssueSummary(string? summary, string? existingIssue)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        return IssueSummaryMerger.Merge(existingIssue, summary);
    }
}
