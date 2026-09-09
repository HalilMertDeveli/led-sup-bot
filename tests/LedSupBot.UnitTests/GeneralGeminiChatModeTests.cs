using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Application.Workflow.Parsing;
using LedSupBot.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LedSupBot.UnitTests;

public sealed class GeneralGeminiChatModeTests
{
    private readonly HybridResponseComposer _composer = new(NullLogger<HybridResponseComposer>.Instance);

    [Theory]
    [InlineData("Türkiye'de 81 il var mı?")]
    [InlineData("4 kişiyiz")]
    [InlineData("2+2 kaç?")]
    [InlineData("1923 yılında ne oldu?")]
    [InlineData("saat 5 gibi gelirim")]
    [InlineData("100 lira param var")]
    [InlineData("3 gün önce konuştuk")]
    public void ReceiverCountParser_Rejects_Unrelated_Numbers_In_General_Sentences(string text)
    {
        var matched = ReceiverCountParser.TryParse(text, out var count);
        Assert.False(matched, $"Expected '{text}' not to parse as receiver count, but got {count}");
    }

    [Theory]
    [InlineData("benim boyum 180 cm")]
    [InlineData("boyum 180")]
    [InlineData("kilom 80 boyum 175")]
    [InlineData("yaşım 30")]
    [InlineData("2+2 kaç")]
    [InlineData("Türkiye'nin başkenti neresi?")]
    [InlineData("Dünyanın en büyük hayvanı ne?")]
    public void ScreenSizeParser_Rejects_Human_Or_Unrelated_Measurements(string text)
    {
        var result = ScreenSizeParser.Parse(text);
        Assert.False(result.ParsedAny, $"Expected '{text}' not to parse as screen size.");
    }

    [Theory]
    [InlineData("Türkiye'nin başkenti neresi?")]
    [InlineData("Dünyanın en büyük hayvanı ne?")]
    [InlineData("2+2 kaç?")]
    [InlineData("Bu akşam ne yemek yesem?")]
    [InlineData("Bir şaka yap")]
    public void TechnicalContentHeuristic_Identifies_General_Chat_As_Non_Technical(string text)
    {
        var isTechnical = TechnicalContentHeuristic.MightContainTechnicalInfo(text);
        Assert.False(isTechnical, $"Expected '{text}' to be recognized as non-technical/general conversation.");
    }

    [Fact]
    public void General_Knowledge_Reply_Is_Delivered_Directly_Without_Workflow_Interference()
    {
        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.AskScreenSize,
            StageLabel = "Ekran ölçüsü",
            KnownFacts = [],
            MissingFacts = ["Ekran ölçüsü"],
            RecentMessages = []
        };

        var slots = new WorkflowSlotState();

        var aiPlan = new ValidatedAiResponsePlan(
            Intent: ConversationIntent.GeneralKnowledge,
            Reply: "Mavi balina. Şu ana kadar yaşamış en büyük hayvan olarak biliniyor.",
            IssueSummary: null,
            NeedsHuman: false,
            SuggestedNextAction: null);

        var input = new HybridResponseComposeInput(
            context,
            slots,
            ConversationStage.AskScreenSize,
            new WorkflowInboundMessage(Guid.NewGuid(), "905425192119", Guid.NewGuid(), "msg-1", MessageType.Text, "Dünyanın en büyük hayvanı ne?", null),
            DeterministicClarifications: [],
            AiPlan: aiPlan,
            ConfigExampleAssetAvailable: false,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: null);

        var result = _composer.Compose(input);

        Assert.Single(result.OutboundTexts);
        Assert.Equal("Mavi balina. Şu ana kadar yaşamış en büyük hayvan olarak biliniyor.", result.OutboundTexts[0]);
        Assert.DoesNotContain("ölçü", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("receiver", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Calculation_Question_Replied_Without_Appending_Workflow_Question()
    {
        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.AskReceiverCount,
            StageLabel = "Receiver sayısı",
            KnownFacts = ["Ekran: 320×160 cm", "Panel etiket fotoğrafı alındı"],
            MissingFacts = ["Receiver adedi/marka", "Config bağlantı durumu", "AnyDesk adresi"],
            Screen = new ContextScreen("320 × 160 cm", Complete: true),
            RecentMessages = []
        };

        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
        };

        var aiPlan = new ValidatedAiResponsePlan(
            Intent: ConversationIntent.GeneralKnowledge,
            Reply: "4",
            IssueSummary: null,
            NeedsHuman: false,
            SuggestedNextAction: null);

        var input = new HybridResponseComposeInput(
            context,
            slots,
            ConversationStage.AskReceiverCount,
            new WorkflowInboundMessage(Guid.NewGuid(), "905425192119", Guid.NewGuid(), "msg-2", MessageType.Text, "2+2 kaç?", null),
            DeterministicClarifications: [],
            AiPlan: aiPlan,
            ConfigExampleAssetAvailable: false,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: null);

        var result = _composer.Compose(input);

        Assert.Single(result.OutboundTexts);
        Assert.Equal("4", result.OutboundTexts[0]);
        Assert.DoesNotContain("receiver", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Casual_Humor_Is_Delivered_Without_Forcing_Workflow()
    {
        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.AskConfigConnection,
            StageLabel = "Config bağlantısı",
            KnownFacts = ["Ekran: 320×160 cm", "Panel etiket fotoğrafı alındı", "Receiver: 10 adet E120"],
            MissingFacts = ["Config bağlantı durumu", "AnyDesk adresi"],
            RecentMessages = []
        };

        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
            ReceiverCount = 10,
        };

        var aiPlan = new ValidatedAiResponsePlan(
            Intent: ConversationIntent.CasualConversation,
            Reply: "LED ekran tamircisinin en büyük korkusu ne? Sorunun 'kablodan değilmiş' çıkması.",
            IssueSummary: null,
            NeedsHuman: false,
            SuggestedNextAction: null);

        var input = new HybridResponseComposeInput(
            context,
            slots,
            ConversationStage.AskConfigConnection,
            new WorkflowInboundMessage(Guid.NewGuid(), "905425192119", Guid.NewGuid(), "msg-3", MessageType.Text, "Bana bir şaka yap", null),
            DeterministicClarifications: [],
            AiPlan: aiPlan,
            ConfigExampleAssetAvailable: true,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: "path/to/img.png",
            ResponseAction: ConversationResponseAction.AnswerOnly,
            CanonicalIntent: ConversationIntent.CasualConversation);

        var result = _composer.Compose(input);

        Assert.Single(result.OutboundTexts);
        Assert.Contains("korkusu", result.OutboundTexts[0]);
        Assert.Null(result.ConfigExampleImagePath);
        Assert.False(result.ConfigExampleMarkedSent);
    }
}
