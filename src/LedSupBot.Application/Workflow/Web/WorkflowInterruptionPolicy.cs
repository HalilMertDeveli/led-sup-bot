using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Application.Workflow.Diagnostics;

namespace LedSupBot.Application.Workflow.Web;

public static class WorkflowInterruptionPolicy
{
    public static bool ShouldResume(
        ConversationContext context,
        ConversationIntent intent,
        ConversationResponseAction action,
        DiagnosticState diagnosticState,
        string? message,
        bool aiRequestedWorkflowResume = false)
    {
        if (!aiRequestedWorkflowResume ||
            action != ConversationResponseAction.AnswerOnly ||
            intent is not (ConversationIntent.GeneralKnowledge or ConversationIntent.CasualConversation) ||
            context.PendingFact == PendingWorkflowFact.None ||
            ConversationSemantics.IsWaitAcknowledgement(message)) return false;
        if (diagnosticState.Issues.Any(x => x.Id == diagnosticState.ActiveIssueId && x.Status != DiagnosticIssueStatus.Resolved)) return false;

        var beforeInbound = context.RecentMessages.Take(Math.Max(0, context.RecentMessages.Count - 1)).LastOrDefault();
        return beforeInbound?.Role == "bot" &&
            beforeInbound.Content.Contains('?', StringComparison.Ordinal) &&
            !beforeInbound.Content.Contains("\n\n", StringComparison.Ordinal);
    }

    public static string? BuildReturn(PendingWorkflowFact fact) => fact switch
    {
        PendingWorkflowFact.ScreenDimensions => "Bu arada ekran ölçülerinde kalmıştık. Yatay ve dikey ölçü kaç cm?",
        PendingWorkflowFact.PanelLabel => "Bu arada panel etiketinde kalmıştık. Fotoğrafını gönderebilir misiniz?",
        PendingWorkflowFact.ReceiverCount => "Bu arada receiver tarafında kalmıştık. Kaç tane kullanıyorsunuz?",
        PendingWorkflowFact.ReceiverModel => "Bu arada receiver modelinde kalmıştık. Kartın modeli nedir?",
        PendingWorkflowFact.PlayerType => "Bu arada oynatıcı tarafında kalmıştık. Playbox mı, processor mü?",
        PendingWorkflowFact.ProcessorHdmi => "Bu arada HDMI bağlantısında kalmıştık. Bağlantıyı yaptınız mı?",
        PendingWorkflowFact.ConfigConnection => "Bu arada config bağlantısında kalmıştık. USB config kablosu bağlı mı?",
        PendingWorkflowFact.AnyDesk => "Bu arada AnyDesk adresinde kalmıştık. Adresi paylaşabilir misiniz?",
        _ => null,
    };
}
