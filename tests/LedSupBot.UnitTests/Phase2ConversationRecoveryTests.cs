using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace LedSupBot.UnitTests;

public sealed class Phase2ConversationRecoveryTests
{
    [Theory]
    [InlineData("devam edelim")]
    [InlineData("nerede kalmıştık?")]
    [InlineData("kuruluma dönelim")]
    public void Resume_Language_Is_Canonical(string text) =>
        Assert.Equal(ConversationIntent.ResumeWorkflow, ConversationSemantics.Detect(text));

    [Theory]
    [InlineData("bunu zaten söyledim")]
    [InlineData("ekran ölçüsünü zaten verdim receiverı soruyordun")]
    [InlineData("ne alaka?")]
    [InlineData("yanlış anladın")]
    public void Recovery_Language_Is_Canonical(string text) =>
        Assert.Equal(ConversationIntent.FlowRecovery, ConversationSemantics.Detect(text));

    [Theory]
    [InlineData("Verdim ya ölçüyü.", ConversationIntent.FlowRecovery)]
    [InlineData("Aynı şeyi niye tekrar soruyorsun?", ConversationIntent.FlowRecovery)]
    [InlineData("Bir dakika, az önce başka bir şey soruyordum.", ConversationIntent.FlowRecovery)]
    [InlineData("Hangi kablo?", ConversationIntent.QuestionAboutCurrentStep)]
    [InlineData("Nereden bakacağım?", ConversationIntent.QuestionAboutCurrentStep)]
    [InlineData("Anlamadım.", ConversationIntent.QuestionAboutCurrentStep)]
    [InlineData("Bekle bi bakıyorum.", ConversationIntent.Acknowledgement)]
    [InlineData("Foto atsam anlar mısın?", ConversationIntent.Acknowledgement)]
    public void Golden_Language_Maps_To_Canonical_Intent(string text, ConversationIntent expected) =>
        Assert.Equal(expected, ConversationSemantics.Detect(text));

    [Theory]
    [InlineData("Ne alaka, ben onu sormadım.", "Siz neyi soruyordunuz?")]
    [InlineData("Yanlış anladın.", "Tam olarak konuyu özetler misiniz?")]
    [InlineData("Bir dakika, az önce başka bir şey soruyordum.", "önceki konuya dönelim")]
    public void Golden_Recovery_Returns_Control_Without_Workflow_Prompt(string text, string expected)
    {
        var input = Input(PendingWorkflowFact.ReceiverCount, ConversationResponseAction.RecoveryResponse, null) with
        {
            Inbound = new WorkflowInboundMessage(Guid.NewGuid(), "905551234567", Guid.NewGuid(), "golden", MessageType.Text, text)
        };
        var reply = Assert.Single(new HybridResponseComposer(NullLogger<HybridResponseComposer>.Instance).Compose(input).OutboundTexts);
        Assert.Contains(expected, reply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Kaç tane receiver", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Bekle bi bakıyorum.", "Tamam, bekliyorum.")]
    [InlineData("Foto atsam anlar mısın?", "Evet, etiketi net görecek şekilde gönderin.")]
    public void Golden_Acknowledgements_Hold_Without_Extra_Question(string text, string expected)
    {
        var input = Input(PendingWorkflowFact.ReceiverCount, ConversationResponseAction.AnswerOnly, null) with
        {
            Inbound = new WorkflowInboundMessage(Guid.NewGuid(), "905551234567", Guid.NewGuid(), "golden-ack", MessageType.Text, text),
            CanonicalIntent = ConversationIntent.Acknowledgement
        };
        Assert.Equal(expected, Assert.Single(new HybridResponseComposer(NullLogger<HybridResponseComposer>.Instance).Compose(input).OutboundTexts));
    }

    [Fact]
    public void Technical_Interruption_Answers_And_Holds_Pending_ReceiverCount()
    {
        var input = Input(PendingWorkflowFact.ReceiverCount, ConversationResponseAction.AnswerAndHoldWorkflow,
            new ValidatedAiResponsePlan(ConversationIntent.LedTroubleshooting,
                "Önce görüntünün kesildiği ilk receiverın data kablosunu kontrol edin.", null, false, null));

        var result = new HybridResponseComposer(NullLogger<HybridResponseComposer>.Instance).Compose(input);

        Assert.Single(result.OutboundTexts);
        Assert.DoesNotContain("Kaç tane", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PendingWorkflowFact.ReceiverCount, input.PendingFact);
    }

    [Fact]
    public void Technical_Interruption_Holds_When_Ai_Is_Unavailable()
    {
        var input = Input(PendingWorkflowFact.ReceiverCount, ConversationResponseAction.AnswerAndHoldWorkflow, null);
        var reply = Assert.Single(new HybridResponseComposer(NullLogger<HybridResponseComposer>.Instance).Compose(input).OutboundTexts);
        Assert.DoesNotContain("receiver", reply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("netleştirelim", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Clarify_Current_Step_Is_Authoritative_Over_Usable_Ai_Text()
    {
        var ai = new ValidatedAiResponsePlan(ConversationIntent.QuestionAboutCurrentStep,
            "Kaç tane receiver var?", null, false, null);
        var input = Input(PendingWorkflowFact.ReceiverModel, ConversationResponseAction.ClarifyCurrentStep, ai,
            new WorkflowSlotState { ReceiverCount = 4 });
        var reply = Assert.Single(new HybridResponseComposer(NullLogger<HybridResponseComposer>.Instance).Compose(input).OutboundTexts);
        Assert.Contains("Panelin arkasında", reply);
        Assert.DoesNotContain("Kaç tane", reply);
    }

    [Fact]
    public void Config_Sequence_Is_Ask_With_Example_Then_AnyDesk_After_Confirmation()
    {
        var baseSlots = new WorkflowSlotState
        {
            WidthCm = 320, HeightCm = 160, HasPanelLabelPhoto = true, ReceiverCount = 4,
            ReceiverModel = ReceiverModel.E120, PlayerType = PlayerType.Playbox
        };
        Assert.Equal(ConversationStage.AskConfigConnection,
            WorkflowStageResolver.ResolveEffectiveStage(baseSlots, true, false));
        var confirmed = new WorkflowSlotState
        {
            WidthCm = 320, HeightCm = 160, HasPanelLabelPhoto = true, ReceiverCount = 4,
            ReceiverModel = ReceiverModel.E120, PlayerType = PlayerType.Playbox, ConfigConfirmed = true
        };
        Assert.Equal(ConversationStage.AskAnyDesk,
            WorkflowStageResolver.ResolveEffectiveStage(confirmed, true, true));
    }

    [Fact]
    public void Resume_Uses_Backend_Pending_Fact_Not_Ai_Reply()
    {
        var input = Input(PendingWorkflowFact.ReceiverCount, ConversationResponseAction.ResumeWorkflow,
            new ValidatedAiResponsePlan(ConversationIntent.ResumeWorkflow, "Ekran ölçüsü nedir?", null, false, null));

        var result = new HybridResponseComposer(NullLogger<HybridResponseComposer>.Instance).Compose(input);

        Assert.Contains("receiver", Assert.Single(result.OutboundTexts), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ölçü", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recovery_Uses_Known_Screen_And_Pending_Receiver()
    {
        var input = Input(PendingWorkflowFact.ReceiverCount, ConversationResponseAction.RecoveryResponse, null,
            new WorkflowSlotState { WidthCm = 320, HeightCm = 160 });
        var result = new HybridResponseComposer(NullLogger<HybridResponseComposer>.Instance).Compose(input);
        var reply = Assert.Single(result.OutboundTexts);
        Assert.Contains("Receiver tarafında", reply);
        Assert.DoesNotContain("yatay ve dikey", reply);
    }

    [Fact]
    public void False_Memory_Does_Not_Fabricate_Screen()
    {
        var input = Input(PendingWorkflowFact.ScreenDimensions, ConversationResponseAction.RecoveryResponse, null);
        var reply = Assert.Single(new HybridResponseComposer(NullLogger<HybridResponseComposer>.Instance).Compose(input).OutboundTexts);
        Assert.Contains("tamamlanmış görünmüyor", reply);
        Assert.False(input.Slots.IsScreenComplete);
    }

    [Fact]
    public void Current_Step_Question_Explains_Receiver_Without_Filling_Model()
    {
        var slots = new WorkflowSlotState { ReceiverCount = 4 };
        var input = Input(PendingWorkflowFact.ReceiverModel, ConversationResponseAction.ClarifyCurrentStep, null, slots);
        var reply = Assert.Single(new HybridResponseComposer(NullLogger<HybridResponseComposer>.Instance).Compose(input).OutboundTexts);
        Assert.Contains("Panelin arkasında", reply);
        Assert.False(slots.IsReceiverModelKnown);
    }

    [Fact]
    public void Pending_Facts_Are_Derived_Independently()
    {
        var slots = new WorkflowSlotState { WidthCm = 320, HeightCm = 160, HasPanelLabelPhoto = true, ReceiverCount = 4 };
        var stage = WorkflowStageResolver.ResolveEffectiveStage(slots, false, true);
        Assert.Equal(ConversationStage.AskReceiverModel, stage);
        Assert.Equal(PendingWorkflowFact.ReceiverModel, WorkflowStageResolver.ResolvePendingFact(stage));

        slots = new WorkflowSlotState { WidthCm = 320, HeightCm = 160, HasPanelLabelPhoto = true, ReceiverCount = 4, ReceiverModel = ReceiverModel.E120 };
        stage = WorkflowStageResolver.ResolveEffectiveStage(slots, false, true);
        Assert.Equal(PendingWorkflowFact.PlayerType, WorkflowStageResolver.ResolvePendingFact(stage));

        slots = new WorkflowSlotState { WidthCm = 320, HeightCm = 160, HasPanelLabelPhoto = true, ReceiverCount = 4, ReceiverModel = ReceiverModel.E120, PlayerType = PlayerType.Processor };
        stage = WorkflowStageResolver.ResolveEffectiveStage(slots, false, true);
        Assert.Equal(PendingWorkflowFact.ProcessorHdmi, WorkflowStageResolver.ResolvePendingFact(stage));
    }

    [Fact]
    public void Completed_Case_Thanks_Does_Not_Start_Questionnaire()
    {
        var input = Input(PendingWorkflowFact.ScreenDimensions, ConversationResponseAction.AnswerOnly, null) with
        {
            Context = new ConversationContext { CompletedCasesCount = 1, PendingFact = PendingWorkflowFact.ScreenDimensions },
            CanonicalIntent = ConversationIntent.Acknowledgement,
        };
        var reply = Assert.Single(new HybridResponseComposer(NullLogger<HybridResponseComposer>.Instance).Compose(input).OutboundTexts);
        Assert.Contains("Rica ederim", reply);
        Assert.DoesNotContain("ölçü", reply, StringComparison.OrdinalIgnoreCase);
    }

    private static HybridResponseComposeInput Input(
        PendingWorkflowFact pending,
        ConversationResponseAction action,
        ValidatedAiResponsePlan? plan,
        WorkflowSlotState? slots = null)
    {
        slots ??= new WorkflowSlotState();
        var stage = pending switch
        {
            PendingWorkflowFact.ReceiverCount => ConversationStage.AskReceiverCount,
            PendingWorkflowFact.ReceiverModel => ConversationStage.AskReceiverModel,
            _ => ConversationStage.AskScreenSize,
        };
        return new HybridResponseComposeInput(
            new ConversationContext { CurrentStage = stage, PendingFact = pending }, slots, stage,
            new WorkflowInboundMessage(Guid.NewGuid(), "905551234567", Guid.NewGuid(), "p2", MessageType.Text, "test"),
            [], plan, false, true, null, action, pending);
    }
}
