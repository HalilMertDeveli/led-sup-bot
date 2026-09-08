using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow.Knowledge;
using LedSupBot.Application.Workflow.Web;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Application.Workflow.Diagnostics;
using LedSupBot.Application.Workflow;

namespace LedSupBot.UnitTests;

public sealed class InformationSourceRoutingPolicyTests
{
    private static readonly WebGroundingOptions Options = new();
    private static readonly KnowledgeGroundingResult NoRag = new(false, false, false, [], [], null, false);

    [Fact]
    public void Current_exchange_rate_uses_web() =>
        Assert.Equal(InformationSourceRoute.WebSearch,
            InformationSourceRoutingPolicy.Decide("Bugün dolar kaç?", ConversationIntent.GeneralKnowledge, NoRag, Options));

    [Fact]
    public void Newest_product_question_uses_web() =>
        Assert.Equal(InformationSourceRoute.WebSearch,
            InformationSourceRoutingPolicy.Decide("Colorlight'ın en yeni controllerı hangisi?", ConversationIntent.LedTechnicalQuestion, NoRag, Options));

    [Fact]
    public void Timeless_general_knowledge_does_not_use_web() =>
        Assert.Equal(InformationSourceRoute.NoSearch,
            InformationSourceRoutingPolicy.Decide("DNS nedir?", ConversationIntent.GeneralKnowledge, NoRag, Options));

    [Theory]
    [InlineData("Bugün nasılsın?", ConversationIntent.CasualConversation)]
    [InlineData("Şu an ekran çalışmıyor.", ConversationIntent.LedTroubleshooting)]
    [InlineData("Şimdi düzeldi.", ConversationIntent.LedTroubleshooting)]
    public void Conversational_or_live_symptom_phrasing_does_not_trigger_web(string message, ConversationIntent intent) =>
        Assert.Equal(InformationSourceRoute.NoSearch,
            InformationSourceRoutingPolicy.Decide(message, intent, NoRag, Options));

    [Fact]
    public void Exact_rag_fact_remains_authoritative()
    {
        var rag = NoRag with { Attempted = true, Used = true, Level = GroundingLevel.GroundedExact };
        Assert.Equal(InformationSourceRoute.NoSearch,
            InformationSourceRoutingPolicy.Decide("X20 maksimum kaç piksel destekliyor?", ConversationIntent.LedTechnicalQuestion, rag, Options));
    }

    [Fact]
    public void Current_firmware_combines_rag_and_web()
    {
        var rag = NoRag with { Attempted = true, Used = true, Level = GroundingLevel.GroundedExact };
        Assert.Equal(InformationSourceRoute.RagAndWeb,
            InformationSourceRoutingPolicy.Decide("X20 için şu an en güncel firmware hangisi?", ConversationIntent.LedTechnicalQuestion, rag, Options));
    }

    [Fact]
    public void Card_question_with_general_rag_can_use_rag_and_web()
    {
        var rag = NoRag with { Attempted = true, Used = true, Level = GroundingLevel.GroundedGeneral };
        Assert.Equal(InformationSourceRoute.RagAndWeb,
            InformationSourceRoutingPolicy.Decide("E120 kartı ne işe yarıyor?", ConversationIntent.LedTechnicalQuestion, rag, Options));
    }

    [Fact]
    public void Card_question_without_rag_can_use_web_without_fabricating_a_package_selection() =>
        Assert.Equal(InformationSourceRoute.WebSearch,
            InformationSourceRoutingPolicy.Decide("E120 kaç pixel destekliyor?", ConversationIntent.LedTechnicalQuestion, NoRag, Options));

    [Fact]
    public void Wait_message_never_searches() =>
        Assert.Equal(InformationSourceRoute.NoSearch,
            InformationSourceRoutingPolicy.Decide("Bekle bi bakıyorum.", ConversationIntent.Acknowledgement, NoRag, Options));

    [Fact]
    public void Public_query_removes_private_identifiers()
    {
        var query = InformationSourceRoutingPolicy.BuildPublicQuery(
            "Müşteri +90 555 123 45 67 AnyDesk adresi 123456789; token=secret-value " +
            @"C:\Users\customer\private\notes.txt X20 latest firmware caseId=deadbeef-1234");
        Assert.DoesNotContain("555", query);
        Assert.DoesNotContain("123456789", query);
        Assert.DoesNotContain("deadbeef", query);
        Assert.DoesNotContain("secret-value", query);
        Assert.DoesNotContain(@"C:\Users", query);
        Assert.Contains("X20 latest firmware", query);
    }

    [Fact]
    public void General_interruption_keeps_pending_fact_silent_without_ai_resume_decision()
    {
        var context = new ConversationContext
        {
            PendingFact = PendingWorkflowFact.ReceiverCount,
            RecentMessages =
            [
                new("bot", "Kaç receiver kullanıyorsunuz?", DateTimeOffset.UtcNow.AddSeconds(-2)),
                new("customer", "DNS nedir?", DateTimeOffset.UtcNow),
            ],
        };
        Assert.False(WorkflowInterruptionPolicy.ShouldResume(context, ConversationIntent.GeneralKnowledge,
            ConversationResponseAction.AnswerOnly, new DiagnosticState(), "DNS nedir?"));
        Assert.Equal(PendingWorkflowFact.ReceiverCount, context.PendingFact);
    }

    [Fact]
    public void Ai_can_explicitly_resume_the_authoritative_pending_fact()
    {
        var context = new ConversationContext
        {
            PendingFact = PendingWorkflowFact.ReceiverCount,
            RecentMessages =
            [
                new("bot", "Kaç receiver kullanıyorsunuz?", DateTimeOffset.UtcNow.AddSeconds(-2)),
                new("customer", "DNS nedir?", DateTimeOffset.UtcNow),
            ],
        };

        Assert.True(WorkflowInterruptionPolicy.ShouldResume(context, ConversationIntent.GeneralKnowledge,
            ConversationResponseAction.AnswerOnly, new DiagnosticState(), "DNS nedir?", aiRequestedWorkflowResume: true));
        Assert.Contains("Kaç tane", WorkflowInterruptionPolicy.BuildReturn(context.PendingFact));
    }

    [Fact]
    public void Repeated_general_turn_or_active_diagnostic_holds_workflow()
    {
        var context = new ConversationContext
        {
            PendingFact = PendingWorkflowFact.ReceiverCount,
            RecentMessages =
            [
                new("bot", "DNS alan adlarını çözer.", DateTimeOffset.UtcNow.AddSeconds(-2)),
                new("customer", "Biraz daha anlat", DateTimeOffset.UtcNow),
            ],
        };
        Assert.False(WorkflowInterruptionPolicy.ShouldResume(context, ConversationIntent.GeneralKnowledge,
            ConversationResponseAction.AnswerOnly, new DiagnosticState(), "Biraz daha anlat"));

        var issue = new DiagnosticIssue { Status = DiagnosticIssueStatus.Active };
        var diagnostic = new DiagnosticState { ActiveIssueId = issue.Id, Issues = [issue] };
        context = new ConversationContext
        {
            PendingFact = PendingWorkflowFact.ReceiverCount,
            RecentMessages =
            [
                new("bot", "Kaç receiver kullanıyorsunuz?", DateTimeOffset.UtcNow.AddSeconds(-2)),
                new("customer", "Önce bu arızayı çözelim", DateTimeOffset.UtcNow),
            ],
        };
        Assert.False(WorkflowInterruptionPolicy.ShouldResume(context, ConversationIntent.CasualConversation,
            ConversationResponseAction.AnswerOnly, diagnostic, "Önce bu arızayı çözelim"));
    }
}
