using System.IO;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Application.Workflow.Parsing;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LedSupBot.UnitTests;

public sealed class ConfigImageDeliveryAndMaxAttemptsTests
{
    private readonly HybridResponseComposer _composer = new(NullLogger<HybridResponseComposer>.Instance);

    #region 1. Config Image Asset & Delivery Tests

    [Fact]
    public void ConfigAssetResolver_Resolves_Real_Project_Asset()
    {
        var resolved = ConfigAssetResolver.ResolvePath();
        Assert.NotNull(resolved);
        Assert.True(File.Exists(resolved));
        Assert.Contains("usb-a-usb-b-printer-cable", resolved, StringComparison.OrdinalIgnoreCase);
        Assert.True(ConfigAssetResolver.IsAssetAvailable());
    }

    [Fact]
    public void Config_Cable_Step_First_Entry_Automatically_Attaches_Image()
    {
        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
            ReceiverCount = 4,
            ReceiverModel = ReceiverModel.E120,
            PlayerType = PlayerType.Playbox,
            ConfigConfirmed = false,
            ConfigExampleSent = false,
        };

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.AskConfigConnection,
            StageLabel = "Config bağlantısı",
            KnownFacts = ["Ekran: 320×160 cm", "Panel etiket fotoğrafı alındı", "Receiver: 4 adet E120", "Oynatıcı türü: Playbox"],
            MissingFacts = ["Config bağlantı durumu", "AnyDesk adresi"],
            Config = new ContextConfig("Config bağlantısı bekleniyor", Complete: false),
            RecentMessages = []
        };

        var input = new HybridResponseComposeInput(
            context,
            slots,
            ConversationStage.AskConfigConnection,
            new WorkflowInboundMessage(Guid.NewGuid(), "905425192119", Guid.NewGuid(), "ext-1", MessageType.Text, "playbox"),
            DeterministicClarifications: [],
            AiPlan: null,
            ConfigExampleAssetAvailable: true,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: null);

        var response = _composer.Compose(input);

        // Text contains explanation and follow up
        Assert.NotEmpty(response.OutboundTexts);
        Assert.Contains(response.OutboundTexts, t => t.Contains("USB-B", StringComparison.OrdinalIgnoreCase) || t.Contains("yazıcı", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(response.OutboundTexts, t => t.Contains("Görselini de gönderiyorum", StringComparison.OrdinalIgnoreCase));

        // Real asset attached
        Assert.NotNull(response.ConfigExampleImagePath);
        Assert.True(File.Exists(response.ConfigExampleImagePath));
        Assert.True(response.ConfigExampleMarkedSent);
    }

    [Theory]
    [InlineData("fotoğrafı var mı")]
    [InlineData("kablo fotoğrafı var mı")]
    [InlineData("resim lazım")]
    [InlineData("görseli var mı")]
    [InlineData("kabloyu göster")]
    [InlineData("foto at")]
    [InlineData("resmi atar mısın")]
    public void Explicit_Image_Request_Attaches_Config_Cable_Image(string text)
    {
        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
            ReceiverCount = 4,
            ConfigConfirmed = false,
            ConfigExampleSent = false,
        };

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.AskConfigConnection,
            StageLabel = "Config bağlantısı",
            PendingFact = PendingWorkflowFact.ConfigConnection,
            KnownFacts = ["Ekran: 320×160 cm", "Receiver: 4 adet"],
            MissingFacts = ["Config bağlantı durumu", "AnyDesk adresi"],
            Config = new ContextConfig("Config bağlantısı bekleniyor", Complete: false),
            RecentMessages = []
        };

        var input = new HybridResponseComposeInput(
            context,
            slots,
            ConversationStage.AskConfigConnection,
            new WorkflowInboundMessage(Guid.NewGuid(), "905425192119", Guid.NewGuid(), "ext-2", MessageType.Text, text),
            DeterministicClarifications: [],
            AiPlan: null,
            ConfigExampleAssetAvailable: true,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: null);

        var response = _composer.Compose(input);

        Assert.NotNull(response.ConfigExampleImagePath);
        Assert.True(response.ConfigExampleMarkedSent);
    }

    [Theory]
    [InlineData("resmi tekrar at")]
    [InlineData("fotoğrafı tekrar gönder")]
    [InlineData("görsel gelmedi")]
    [InlineData("tekrar yollar mısın")]
    public void Explicit_Resend_Request_Allows_Resending_Image_Even_If_Previously_Sent(string text)
    {
        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
            ReceiverCount = 4,
            ConfigConfirmed = false,
            ConfigExampleSent = true, // already sent previously
        };

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.AskConfigConnection,
            StageLabel = "Config bağlantısı",
            PendingFact = PendingWorkflowFact.ConfigConnection,
            KnownFacts = ["Ekran: 320×160 cm"],
            MissingFacts = ["Config bağlantı durumu"],
            RecentMessages = []
        };

        var input = new HybridResponseComposeInput(
            context,
            slots,
            ConversationStage.AskConfigConnection,
            new WorkflowInboundMessage(Guid.NewGuid(), "905425192119", Guid.NewGuid(), "ext-3", MessageType.Text, text),
            DeterministicClarifications: [],
            AiPlan: null,
            ConfigExampleAssetAvailable: true,
            ConfigExampleCompleted: true, // already marked sent
            ConfigExampleImagePath: null);

        var response = _composer.Compose(input);

        // Explicit resend MUST attach image
        Assert.NotNull(response.ConfigExampleImagePath);
    }

    [Fact]
    public void Image_Already_Sent_FollowUp_Does_Not_Duplicate_Image()
    {
        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
            ReceiverCount = 4,
            ConfigConfirmed = false,
            ConfigExampleSent = true,
        };

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.AskConfigConnection,
            StageLabel = "Config bağlantısı",
            PendingFact = PendingWorkflowFact.ConfigConnection,
            KnownFacts = ["Ekran: 320×160 cm"],
            MissingFacts = ["Config bağlantı durumu"],
            RecentMessages = []
        };

        var input = new HybridResponseComposeInput(
            context,
            slots,
            ConversationStage.AskConfigConnection,
            new WorkflowInboundMessage(Guid.NewGuid(), "905425192119", Guid.NewGuid(), "ext-4", MessageType.Text, "Nereye bağlayacağım?"),
            DeterministicClarifications: [],
            AiPlan: new ValidatedAiResponsePlan(ConversationIntent.QuestionAboutCurrentStep, "Kabloyu bilgisayarın USB portuna ve cihazın USB-B portuna takmanız gerekiyor.", null, false, null),
            ConfigExampleAssetAvailable: true,
            ConfigExampleCompleted: true,
            ConfigExampleImagePath: null);

        var response = _composer.Compose(input);

        // Image already sent, question was merely "nereye bağlayacağım" -> text only, no image spam!
        Assert.Null(response.ConfigExampleImagePath);
        Assert.False(response.ConfigExampleMarkedSent);
    }

    #endregion

    #region 2. Maximum Two Attempts Per Fact Tests

    [Fact]
    public void Screen_Dimensions_Max_Two_Attempts_Then_Deferred()
    {
        // 0 attempts -> AskScreenSize
        var slots0 = new WorkflowSlotState();
        var stage0 = WorkflowStageResolver.ResolveEffectiveStage(slots0, false, true);
        Assert.Equal(ConversationStage.AskScreenSize, stage0);

        // 1 attempt -> AskScreenSize with rephrase
        var supportCase1 = new TechnicalSupportCase();
        supportCase1.IncrementQuestionAttempts("ScreenDimensions");
        var slots1 = WorkflowSlotState.FromCase(supportCase1, new Conversation());
        Assert.Equal(1, slots1.GetAttempts("ScreenDimensions"));
        Assert.False(slots1.IsScreenDeferred);
        var stage1 = WorkflowStageResolver.ResolveEffectiveStage(slots1, false, true);
        Assert.Equal(ConversationStage.AskScreenSize, stage1);

        // 2 attempts -> Deferred! Advances to AskPanelLabelPhotos
        var supportCase2 = new TechnicalSupportCase();
        supportCase2.IncrementQuestionAttempts("ScreenDimensions");
        supportCase2.IncrementQuestionAttempts("ScreenDimensions");
        var slots2 = WorkflowSlotState.FromCase(supportCase2, new Conversation());
        Assert.Equal(2, slots2.GetAttempts("ScreenDimensions"));
        Assert.True(slots2.IsScreenDeferred);
        Assert.False(slots2.IsScreenComplete); // truth preserved: not complete!

        var stage2 = WorkflowStageResolver.ResolveEffectiveStage(slots2, false, true);
        Assert.Equal(ConversationStage.AskPanelLabelPhotos, stage2);
    }

    [Fact]
    public void Panel_Photo_Max_Two_Attempts_Then_Deferred()
    {
        var supportCase = new TechnicalSupportCase();
        supportCase.IncrementQuestionAttempts("ScreenDimensions");
        supportCase.IncrementQuestionAttempts("ScreenDimensions"); // Screen deferred
        supportCase.IncrementQuestionAttempts("PanelLabel");
        supportCase.IncrementQuestionAttempts("PanelLabel"); // Panel deferred

        var slots = WorkflowSlotState.FromCase(supportCase, new Conversation());
        Assert.True(slots.IsScreenDeferred);
        Assert.True(slots.IsPanelPhotoDeferred);

        // Advances past panel photo to receiver count!
        var stage = WorkflowStageResolver.ResolveEffectiveStage(slots, false, true);
        Assert.Equal(ConversationStage.AskReceiverCount, stage);
    }

    [Fact]
    public void Receiver_Count_And_Model_Max_Two_Attempts_Then_Deferred()
    {
        var supportCase = new TechnicalSupportCase();
        supportCase.IncrementQuestionAttempts("ReceiverCount");
        supportCase.IncrementQuestionAttempts("ReceiverCount"); // count deferred
        supportCase.IncrementQuestionAttempts("ReceiverModel");
        supportCase.IncrementQuestionAttempts("ReceiverModel"); // model deferred

        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
            QuestionAttempts = supportCase.GetAllQuestionAttempts(),
        };

        Assert.True(slots.IsReceiverCountDeferred);
        Assert.True(slots.IsReceiverModelDeferred);

        // Advances past receiver to PlayerType!
        var stage = WorkflowStageResolver.ResolveEffectiveStage(slots, false, true);
        Assert.Equal(ConversationStage.AskPlayerType, stage);
    }

    [Fact]
    public void Config_Connection_Max_Two_Attempts_Then_Deferred()
    {
        var supportCase = new TechnicalSupportCase();
        supportCase.IncrementQuestionAttempts("ConfigConnection");
        supportCase.IncrementQuestionAttempts("ConfigConnection");

        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
            ReceiverCount = 4,
            ReceiverModel = ReceiverModel.E120,
            PlayerType = PlayerType.Playbox,
            ConfigConfirmed = false,
            QuestionAttempts = supportCase.GetAllQuestionAttempts(),
        };

        Assert.True(slots.IsConfigConnectionDeferred);

        // Advances past config connection to AnyDesk!
        var stage = WorkflowStageResolver.ResolveEffectiveStage(slots, true, false);
        Assert.Equal(ConversationStage.AskAnyDesk, stage);
    }

    [Fact]
    public void AnyDesk_Max_Two_Attempts_Then_Advances_To_CollectingFinalInfo()
    {
        var supportCase = new TechnicalSupportCase();
        supportCase.IncrementQuestionAttempts("AnyDesk");
        supportCase.IncrementQuestionAttempts("AnyDesk");

        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
            ReceiverCount = 4,
            ReceiverModel = ReceiverModel.E120,
            PlayerType = PlayerType.Playbox,
            ConfigConfirmed = true,
            HasAnyDesk = false,
            QuestionAttempts = supportCase.GetAllQuestionAttempts(),
        };

        Assert.True(slots.IsAnyDeskDeferred);

        var stage = WorkflowStageResolver.ResolveEffectiveStage(slots, true, true);
        Assert.Equal(ConversationStage.CollectingFinalInfo, stage);
    }

    [Fact]
    public void Question_Attempts_Survive_Serialization_And_Restart()
    {
        var originalCase = new TechnicalSupportCase();
        originalCase.IncrementQuestionAttempts("ScreenDimensions");
        originalCase.IncrementQuestionAttempts("ScreenDimensions");
        originalCase.IncrementQuestionAttempts("ReceiverCount");

        var serialized = originalCase.QuestionAttemptsJson;
        Assert.NotNull(serialized);

        var loadedCase = new TechnicalSupportCase { QuestionAttemptsJson = serialized };
        Assert.Equal(2, loadedCase.GetQuestionAttempts("ScreenDimensions"));
        Assert.Equal(1, loadedCase.GetQuestionAttempts("ReceiverCount"));
        Assert.Equal(0, loadedCase.GetQuestionAttempts("ConfigConnection"));

        var slots = WorkflowSlotState.FromCase(loadedCase, new Conversation());
        Assert.True(slots.IsScreenDeferred);
        Assert.False(slots.IsReceiverCountDeferred);
    }

    [Fact]
    public void Late_Customer_Answer_Clears_Deferred_And_Persists_Fact()
    {
        var supportCase = new TechnicalSupportCase();
        supportCase.IncrementQuestionAttempts("ReceiverCount");
        supportCase.IncrementQuestionAttempts("ReceiverCount");

        var slotsBefore = WorkflowSlotState.FromCase(supportCase, new Conversation());
        Assert.True(slotsBefore.IsReceiverCountDeferred);
        Assert.False(slotsBefore.IsReceiverCountKnown);

        // Customer voluntarily provides the fact later: "10 receiver var"
        Assert.True(ReceiverCountParser.TryParse("10 receiver var", out var count));
        Assert.Equal(10, count);

        // Fact is persisted on case
        supportCase.ReceiverInformation = new ReceiverInformation { Count = count };

        var slotsAfter = WorkflowSlotState.FromCase(supportCase, new Conversation());
        Assert.True(slotsAfter.IsReceiverCountKnown);
        Assert.Equal(10, slotsAfter.ReceiverCount);
    }

    #endregion

    #region 3. Truthful Case Summary Tests

    [Fact]
    public void Truthful_Summary_Distinguishes_Collected_Vs_Unresolved_Facts()
    {
        // Case completed where Panel Label and Config were deferred/unresolved
        var summary = WorkflowPrompts.BuildSummary(
            width: 320,
            height: 160,
            receiverCount: 10,
            receiverModel: "E120",
            playerType: PlayerType.Playbox,
            processorHdmiConnected: null,
            configConfirmed: false,
            photosReceived: false);

        Assert.Contains("320 x 160 cm", summary);
        Assert.Contains("10 adet E120", summary);
        Assert.Contains("Playbox", summary);
        Assert.Contains("Panel etiket fotoğrafları: Alınamadı", summary);
        Assert.Contains("Config bağlantısı: Alınamadı", summary);
        Assert.Contains("AnyDesk: Alındı", summary);

        // Closing line must be neutral because required facts were incomplete
        Assert.Contains("Paylaştığınız bilgileri kaydettim, teşekkürler.", summary);
        Assert.DoesNotContain("Gerekli teknik bilgileri aldım", summary);
    }

    [Fact]
    public void Truthful_Summary_All_Collected_Uses_Complete_Closing()
    {
        var summary = WorkflowPrompts.BuildSummary(
            width: 500,
            height: 300,
            receiverCount: 8,
            receiverModel: null,
            playerType: null,
            processorHdmiConnected: null,
            configConfirmed: true,
            photosReceived: true);

        Assert.Contains("500 x 300 cm", summary);
        Assert.Contains("8 adet", summary);
        Assert.Contains("Panel etiket fotoğrafları: Alındı", summary);
        Assert.Contains("Config bağlantısı: Yapıldı", summary);
        Assert.Contains("AnyDesk: Alındı", summary);
        Assert.Contains("Gerekli teknik bilgileri aldım, teşekkürler.", summary);
    }

    #endregion
}
