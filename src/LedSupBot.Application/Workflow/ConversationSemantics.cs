using System.Globalization;
using System.Text;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow.Context;

namespace LedSupBot.Application.Workflow;

public enum ConversationResponseAction
{
    WorkflowPrompt = 0,
    AnswerAndHoldWorkflow,
    ResumeWorkflow,
    ClarifyCurrentStep,
    RecoveryResponse,
    AnswerOnly,
}

public static class ConversationSemantics
{
    public static ConversationIntent Detect(string? message)
    {
        var text = Normalize(message);
        if (string.IsNullOrWhiteSpace(text)) return ConversationIntent.Unclear;

        if (ContainsAny(text, "devam", "nerede kalmistik", "kuruluma donelim", "receivera donelim", "receiver tarafina donelim"))
            return ConversationIntent.ResumeWorkflow;

        if (ContainsAny(text, "zaten soyledim", "niye tekrar soruyorsun", "ne alaka", "ben onu sormadim",
                "soruyordun", "yanlis anladin", "geri donelim", "onceki konuya donelim"))
            return ConversationIntent.FlowRecovery;

        if (ContainsAny(text, "receiver kart hangisi", "receiver hangisi", "config kablosu nedir", "anydesk nedir", "nereye takacagim"))
            return ConversationIntent.QuestionAboutCurrentStep;

        if (ContainsAny(text, "pardon", "degil", "yanlis yazdim"))
            return ConversationIntent.Correction;

        if (ContainsAny(text, "tesekkur", "sagol", "sag ol"))
            return ConversationIntent.Acknowledgement;

        if (ContainsAny(text, "bir sey daha soracagim", "bir soru soracagim"))
            return ConversationIntent.CasualConversation;

        if (ContainsAny(text, "yarisi", "goruntu yok", "gelmiyor", "calismiyor", "bozuk", "titriyor", "renk bozuk"))
            return ConversationIntent.LedTroubleshooting;

        return ConversationIntent.Unclear;
    }

    public static ConversationResponseAction ResolveAction(ConversationIntent intent) => intent switch
    {
        ConversationIntent.LedTechnicalQuestion or ConversationIntent.LedTroubleshooting => ConversationResponseAction.AnswerAndHoldWorkflow,
        ConversationIntent.QuestionAboutCurrentStep => ConversationResponseAction.ClarifyCurrentStep,
        ConversationIntent.ResumeWorkflow => ConversationResponseAction.ResumeWorkflow,
        ConversationIntent.FlowRecovery => ConversationResponseAction.RecoveryResponse,
        ConversationIntent.CasualConversation or ConversationIntent.GeneralKnowledge or ConversationIntent.Acknowledgement => ConversationResponseAction.AnswerOnly,
        _ => ConversationResponseAction.WorkflowPrompt,
    };

    public static ConversationIntent PreferBackendRecognized(ConversationIntent local, ConversationIntent ai) =>
        local == ConversationIntent.Unclear ? ai : local;

    private static bool ContainsAny(string value, params string[] candidates) => candidates.Any(value.Contains);

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.ToLower(new CultureInfo("tr-TR")).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c == 'ı' ? 'i' : c);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
