using System.Globalization;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Application.Workflow.Parsing;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LedSupBot.UnitTests;

public sealed class SemanticConversationEngineTests
{
    private readonly HybridResponseComposer _composer = new(NullLogger<HybridResponseComposer>.Instance);

    #region Scenario 1: Screen Dimensions (Semantic, Natural Turkish & Typos)

    [Fact]
    public void Repro_Step1_Dikeyde_160_Yatayda_320_Parses_Correctly()
    {
        var result = ScreenSizeParser.Parse("Dikeyde 160 cm, yatayda 320 cm");

        Assert.True(result.ParsedAny);
        Assert.Equal(320, result.WidthCm);
        Assert.Equal(160, result.HeightCm);
        Assert.False(result.Ambiguous);
    }

    [Theory]
    [InlineData("Dikeyde 160 cm, yatayda 320 cm", 320, 160)]
    [InlineData("dikeyde 160 cm yatayda 320 cm", 320, 160)]
    [InlineData("yatay 320 dikey 160", 320, 160)]
    [InlineData("320 yatay 160 dikey", 320, 160)]
    [InlineData("eni 320 boyu 160", 320, 160)]
    [InlineData("en 320 boy 160", 320, 160)]
    [InlineData("boy 160 en 320", 320, 160)]
    [InlineData("boyu 160 cm eni 320 cm", 320, 160)]
    [InlineData("320 genişlik 160 yükseklik", 320, 160)]
    [InlineData("160 yükseklik 320 genişlik", 320, 160)]
    [InlineData("320cm en 160cm boy", 320, 160)]
    [InlineData("160cm boy 320cm en", 320, 160)]
    [InlineData("320x160", 320, 160)]
    [InlineData("320*160", 320, 160)]
    [InlineData("320 × 160", 320, 160)]
    [InlineData("320 x 160 cm", 320, 160)]
    [InlineData("3.2 metre genişlik 1.6 metre yükseklik", 320, 160)]
    [InlineData("3.2 metre en 1.6 metre boy", 320, 160)]
    [InlineData("1.6 metre boy 3.2 metre en", 320, 160)]
    [InlineData("3.2m x 1.6m", 320, 160)]
    [InlineData("320'ye 160", 320, 160)]
    [InlineData("320 ye 160", 320, 160)]
    [InlineData("320 e 160", 320, 160)]
    [InlineData("320 160 cm", 320, 160)]
    [InlineData("dikeu 160 yatay 320", 320, 160)]
    [InlineData("320 genislik 160 yukseklk", 320, 160)]
    public void ScreenSizeParser_Natural_Turkish_And_Typo_Tolerance(string text, decimal expectedWidth, decimal expectedHeight)
    {
        var result = ScreenSizeParser.Parse(text);

        Assert.True(result.ParsedAny);
        Assert.Equal(expectedWidth, result.WidthCm);
        Assert.Equal(expectedHeight, result.HeightCm);
        Assert.False(result.Ambiguous);
    }

    [Fact]
    public void Partial_Dimension_Preserved_And_Contextual_Completion()
    {
        // 1. Customer gives only width: "yatay 320"
        var first = ScreenSizeParser.ParseWithContext("yatay 320", knownWidth: null, knownHeight: null);
        Assert.True(first.ParsedAny);
        Assert.Equal(320, first.WidthCm);
        Assert.Null(first.HeightCm);

        // 2. Later customer gives only height in context: "160"
        var second = ScreenSizeParser.ParseWithContext("160", knownWidth: 320, knownHeight: null);
        Assert.True(second.ParsedAny);
        Assert.Null(second.WidthCm);
        Assert.Equal(160, second.HeightCm);
    }

    [Fact]
    public void Screen_Complete_Never_ReAsks_Dimensions()
    {
        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = false,
        };

        var stage = WorkflowStageResolver.ResolveEffectiveStage(slots, configExampleAssetAvailable: true, configExampleStepCompleted: false);
        Assert.Equal(ConversationStage.AskPanelLabelPhotos, stage);

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = stage,
            StageLabel = "Panel etiket fotoğrafları",
            KnownFacts = ["Ekran: 320×160 cm"],
            MissingFacts = ["Panel etiket fotoğrafı", "Receiver adedi/marka", "Config bağlantı durumu", "AnyDesk adresi"],
            Screen = new ContextScreen("320 × 160 cm", Complete: true),
            RecentMessages = []
        };

        var input = new HybridResponseComposeInput(
            context,
            slots,
            stage,
            new WorkflowInboundMessage(Guid.NewGuid(), "905551234567", Guid.NewGuid(), "ext-1", MessageType.Text, "tamam"),
            DeterministicClarifications: [],
            AiPlan: null,
            ConfigExampleAssetAvailable: true,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: "path/to/img.png");

        var response = _composer.Compose(input);
        Assert.DoesNotContain(response.OutboundTexts, t => t.Contains("Ekran ölçüleriniz nedir", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(response.OutboundTexts, t => t.Contains("kaç cm", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(response.OutboundTexts, t => t.Contains("etiket", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Scenario 2: Receiver Extraction (Count + Model + Manufacturer)

    [Theory]
    [InlineData("10 tane e120 kullandım", 10, ReceiverModel.E120)]
    [InlineData("10 tane E120 var", 10, ReceiverModel.E120)]
    [InlineData("10 adet E120", 10, ReceiverModel.E120)]
    [InlineData("10 tane receiver var", 10, ReceiverModel.Unknown)]
    [InlineData("10 tane kullandım", 10, ReceiverModel.Unknown)]
    [InlineData("10 receiver", 10, ReceiverModel.Unknown)]
    [InlineData("10 tane yeşil kart", 10, ReceiverModel.Unknown)]
    [InlineData("10 yeşil kart var", 10, ReceiverModel.Unknown)]
    [InlineData("receiver 10 adet", 10, ReceiverModel.Unknown)]
    [InlineData("8 receiver var", 8, ReceiverModel.Unknown)]
    [InlineData("8", 8, ReceiverModel.Unknown)]
    [InlineData("8 tane", 8, ReceiverModel.Unknown)]
    [InlineData("8 adet", 8, ReceiverModel.Unknown)]
    [InlineData("colorlight e120 12 tane", 12, ReceiverModel.E120)]
    public void Receiver_Count_And_Model_Parsers(
        string text,
        int expectedCount,
        ReceiverModel expectedModel)
    {
        Assert.True(ReceiverCountParser.TryParse(text, out var count));
        Assert.Equal(expectedCount, count);

        if (expectedModel != ReceiverModel.Unknown)
        {
            Assert.True(ReceiverModelParser.TryParse(text, out var model));
            Assert.Equal(expectedModel, model);
        }
    }

    [Fact]
    public void Receiver_Known_Never_ReAsks_Receiver()
    {
        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
            ReceiverCount = 10,
            ReceiverModel = ReceiverModel.E120,
            PlayerType = PlayerType.Playbox,
            ConfigConfirmed = false,
        };

        var stage = WorkflowStageResolver.ResolveEffectiveStage(slots, configExampleAssetAvailable: true, configExampleStepCompleted: false);
        Assert.Equal(ConversationStage.AskConfigConnection, stage);

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = stage,
            StageLabel = "Config bağlantısı",
            KnownFacts = ["Ekran: 320×160 cm", "Panel etiket fotoğrafı alındı", "Receiver: 10 adet"],
            MissingFacts = ["Config bağlantı durumu", "AnyDesk adresi"],
            Receiver = new ContextReceiver("10 adet", Complete: true),
            RecentMessages = []
        };

        var input = new HybridResponseComposeInput(
            context,
            slots,
            stage,
            new WorkflowInboundMessage(Guid.NewGuid(), "905551234567", Guid.NewGuid(), "ext-2", MessageType.Text, "10 tane e120 kullandım"),
            DeterministicClarifications: [],
            AiPlan: null,
            ConfigExampleAssetAvailable: true,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: "path/to/img.png");

        var response = _composer.Compose(input);
        Assert.DoesNotContain(response.OutboundTexts, t => t.Contains("Kaç adet receiver", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(response.OutboundTexts, t => t.Contains("Receiver sayısını", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(response.OutboundTexts, t => t.Contains("config", StringComparison.OrdinalIgnoreCase) || t.Contains("yazıcı", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Scenario 3 & 4: Config Cable Clarification, Explanations & Intent

    [Theory]
    [InlineData("Config kablosu ne")]
    [InlineData("config kablo ne")]
    [InlineData("cobfig kablosu ne nereye baglayacagim")]
    [InlineData("nereye bağlayacağım")]
    [InlineData("hangi kablo")]
    [InlineData("nasıl bir kablo")]
    [InlineData("kablo nerede")]
    [InlineData("anlamadım")]
    [InlineData("ne demek")]
    [InlineData("nasıl yani")]
    public void ConfigConfirmationParser_Detects_Clarification_Requests(string text)
    {
        var result = ConfigConfirmationParser.Parse(text);
        Assert.Equal(ConfigConfirmationResult.ClarificationRequested, result);
    }

    [Theory]
    [InlineData("bağladım")]
    [InlineData("bagladim")]
    [InlineData("evet")]
    [InlineData("tamam")]
    [InlineData("yaptım")]
    [InlineData("hazır")]
    [InlineData("bağlandı")]
    [InlineData("kabloyu bağladım")]
    [InlineData("taktım")]
    public void ConfigConfirmationParser_Detects_Confirmations(string text)
    {
        var result = ConfigConfirmationParser.Parse(text);
        Assert.Equal(ConfigConfirmationResult.Confirmed, result);
    }

    [Fact]
    public void Config_Clarification_Triggers_Image_And_Does_Not_Repeat_Question()
    {
        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
            ReceiverCount = 10,
            ConfigConfirmed = false,
        };

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.AskConfigConnection,
            StageLabel = "Config bağlantısı",
            KnownFacts = ["Ekran: 320×160 cm", "Panel etiket fotoğrafı alındı", "Receiver: 10 adet"],
            MissingFacts = ["Config bağlantı durumu", "AnyDesk adresi"],
            Config = new ContextConfig("Config bağlantısı bekleniyor", Complete: false),
            RecentMessages = []
        };

        var input = new HybridResponseComposeInput(
            context,
            slots,
            ConversationStage.AskConfigConnection,
            new WorkflowInboundMessage(Guid.NewGuid(), "905551234567", Guid.NewGuid(), "ext-3", MessageType.Text, "Config kablosu ne"),
            DeterministicClarifications: [WorkflowPrompts.ExplainConfigConnection],
            AiPlan: null,
            ConfigExampleAssetAvailable: true,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: "C:\\images\\config-cable.png");

        var response = _composer.Compose(input);

        // Cable reference image MUST be selected
        Assert.NotNull(response.ConfigExampleImagePath);
        Assert.True(response.ConfigExampleMarkedSent);

        // Must NOT repeat original robotic question
        Assert.DoesNotContain(WorkflowPrompts.AskConfigConnection, response.OutboundTexts);
        Assert.Contains(response.OutboundTexts, t => t.Contains("USB-B", StringComparison.OrdinalIgnoreCase) || t.Contains("kareye yakın", StringComparison.OrdinalIgnoreCase) || t.Contains("yazıcı", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Config_Confirmed_Advances_And_Never_Sends_Cable_Instructions_Again()
    {
        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
            ReceiverCount = 10,
            ReceiverModel = ReceiverModel.E120,
            PlayerType = PlayerType.Playbox,
            ConfigConfirmed = true,
            ConfigExampleSent = true,
            HasAnyDesk = false,
        };

        var stage = WorkflowStageResolver.ResolveEffectiveStage(slots, configExampleAssetAvailable: true, configExampleStepCompleted: true);
        Assert.Equal(ConversationStage.AskAnyDesk, stage);

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = stage,
            StageLabel = "AnyDesk",
            KnownFacts = ["Ekran: 320×160 cm", "Panel etiket fotoğrafı alındı", "Receiver: 10 adet", "Config bağlantısı onaylandı"],
            MissingFacts = ["AnyDesk adresi"],
            Config = new ContextConfig("Config bağlantısı tamam", Complete: true),
            AnyDesk = new ContextAnyDesk("AnyDesk bekleniyor", Complete: false),
            RecentMessages = []
        };

        var input = new HybridResponseComposeInput(
            context,
            slots,
            stage,
            new WorkflowInboundMessage(Guid.NewGuid(), "905551234567", Guid.NewGuid(), "ext-4", MessageType.Text, "Bağladım"),
            DeterministicClarifications: [],
            AiPlan: null,
            ConfigExampleAssetAvailable: true,
            ConfigExampleCompleted: true,
            ConfigExampleImagePath: "C:\\images\\config-cable.png");

        var response = _composer.Compose(input);

        // Must NOT attach cable image after confirmed!
        Assert.Null(response.ConfigExampleImagePath);
        Assert.False(response.ConfigExampleMarkedSent);

        // Next prompt is AnyDesk
        Assert.Contains(response.OutboundTexts, t => t.Contains("AnyDesk", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Scenario 5: Multi-Slot Extraction

    [Fact]
    public void Multi_Slot_Extraction_Extracts_All_Facts_In_Single_Message()
    {
        const string text = "10 tane E120 receiver kullandım ve config kablosunu da bağladım";

        Assert.True(ReceiverCountParser.TryParse(text, out var count));
        Assert.Equal(10, count);

        Assert.True(ReceiverModelParser.TryParse(text, out var model));
        Assert.Equal(ReceiverModel.E120, model);

        var config = ConfigConfirmationParser.Parse(text);
        Assert.Equal(ConfigConfirmationResult.Confirmed, config);
    }

    #endregion

    #region Scenario 6: Anti-Repetition / Looping Safety Guard

    [Fact]
    public void Anti_Repetition_Guard_Prevents_Sending_Exact_Same_Prompt_BackToBack()
    {
        var slots = new WorkflowSlotState
        {
            WidthCm = null,
            HeightCm = null,
        };

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.AskScreenSize,
            StageLabel = "Ekran ölçüsü",
            KnownFacts = [],
            MissingFacts = ["Ekran ölçüsü"],
            RecentMessages =
            [
                new ContextMessage("bot", WorkflowPrompts.AskScreenSize, DateTimeOffset.UtcNow.AddMinutes(-1)),
                new ContextMessage("customer", "anlamadım", DateTimeOffset.UtcNow)
            ]
        };

        var input = new HybridResponseComposeInput(
            context,
            slots,
            ConversationStage.AskScreenSize,
            new WorkflowInboundMessage(Guid.NewGuid(), "905551234567", Guid.NewGuid(), "ext-5", MessageType.Text, "anlamadım"),
            DeterministicClarifications: [],
            AiPlan: null,
            ConfigExampleAssetAvailable: false,
            ConfigExampleCompleted: true,
            ConfigExampleImagePath: null);

        var response = _composer.Compose(input);

        // Must not be the exact identical prompt that was just sent!
        Assert.DoesNotContain(WorkflowPrompts.AskScreenSize, response.OutboundTexts);
        Assert.Contains(response.OutboundTexts, t => t.Contains("santimetre", StringComparison.OrdinalIgnoreCase) || t.Contains("en ve boy", StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}
