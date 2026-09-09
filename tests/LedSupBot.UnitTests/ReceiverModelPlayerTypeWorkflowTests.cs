using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Application.Workflow.Parsing;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LedSupBot.UnitTests;

public sealed class ReceiverModelPlayerTypeWorkflowTests
{
    private readonly HybridResponseComposer _composer = new(NullLogger<HybridResponseComposer>.Instance);

    #region TEST A: "4 tane E120 kullandım" -> Count=4, Model=E120, no repeated count/model question

    [Fact]
    public void TestA_Extracts_Count_And_Model_And_Never_ReAsks()
    {
        const string text = "4 tane E120 kullandım";

        Assert.True(ReceiverCountParser.TryParse(text, out var count));
        Assert.Equal(4, count);

        Assert.True(ReceiverModelParser.TryParse(text, out var model));
        Assert.Equal(ReceiverModel.E120, model);

        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
            ReceiverCount = count,
            ReceiverModel = model,
        };

        var stage = WorkflowStageResolver.ResolveEffectiveStage(slots, configExampleAssetAvailable: true, configExampleStepCompleted: false);
        // Both receiver facts known -> must advance past receiver questions directly to player type
        Assert.Equal(ConversationStage.AskPlayerType, stage);

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = stage,
            StageLabel = "Oynatıcı türü",
            KnownFacts = ["Ekran: 320×160 cm", "Panel etiket fotoğrafı alındı", "Receiver: 4 adet E120"],
            MissingFacts = ["Oynatıcı türü (Playbox / Processor)", "Config bağlantı durumu", "AnyDesk adresi"],
            Receiver = new ContextReceiver("4 adet E120", Complete: true, CountComplete: true, ModelComplete: true, Count: 4, Model: ReceiverModel.E120),
            RecentMessages = []
        };

        var input = new HybridResponseComposeInput(
            context,
            slots,
            stage,
            new WorkflowInboundMessage(Guid.NewGuid(), "905551234567", Guid.NewGuid(), "ext-1", MessageType.Text, text),
            DeterministicClarifications: [],
            AiPlan: null,
            ConfigExampleAssetAvailable: true,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: null);

        var response = _composer.Compose(input);

        Assert.Single(response.OutboundTexts);
        Assert.Contains("Playbox", response.OutboundTexts[0]);
        Assert.DoesNotContain("kaç adet", response.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("receiver sayısı", response.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("receiver modeli", response.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region TEST B: "e80 kullanıyorum" -> Model=E80, ask ONLY count if count missing

    [Fact]
    public void TestB_Extracts_Model_And_Asks_Only_Count()
    {
        const string text = "e80 kullanıyorum";

        Assert.True(ReceiverModelParser.TryParse(text, out var model));
        Assert.Equal(ReceiverModel.E80, model);

        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
            ReceiverCount = null,
            ReceiverModel = model,
        };

        var stage = WorkflowStageResolver.ResolveEffectiveStage(slots, configExampleAssetAvailable: true, configExampleStepCompleted: false);
        Assert.Equal(ConversationStage.AskReceiverCount, stage);

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = stage,
            StageLabel = "Receiver sayısı",
            KnownFacts = ["Ekran: 320×160 cm", "Panel etiket fotoğrafı alındı", "Receiver modeli: E80"],
            MissingFacts = ["Receiver adedi"],
            Receiver = new ContextReceiver("E80", Complete: false, CountComplete: false, ModelComplete: true, Count: null, Model: ReceiverModel.E80),
            RecentMessages = []
        };

        var input = new HybridResponseComposeInput(
            context,
            slots,
            stage,
            new WorkflowInboundMessage(Guid.NewGuid(), "905551234567", Guid.NewGuid(), "ext-1", MessageType.Text, text),
            DeterministicClarifications: [],
            AiPlan: null,
            ConfigExampleAssetAvailable: true,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: null);

        var response = _composer.Compose(input);

        Assert.Single(response.OutboundTexts);
        Assert.Contains("E80", response.OutboundTexts[0]);
        Assert.Contains("kaç adet", response.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("modeli hangisi", response.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region TEST C: "5 tane var" -> ReceiverCount=5

    [Fact]
    public void TestC_Extracts_Count_Without_Explicit_Keywords()
    {
        const string text = "5 tane var";

        Assert.True(ReceiverCountParser.TryParse(text, out var count));
        Assert.Equal(5, count);

        Assert.False(ReceiverModelParser.TryParse(text, out var model));
        Assert.Equal(ReceiverModel.Unknown, model);
    }

    #endregion

    #region TEST D: "10 adet 5a75e" -> Count=10, Model=5A75E

    [Fact]
    public void TestD_Extracts_Count_And_5A75E_Model()
    {
        const string text = "10 adet 5a75e";

        Assert.True(ReceiverCountParser.TryParse(text, out var count));
        Assert.Equal(10, count);

        Assert.True(ReceiverModelParser.TryParse(text, out var model));
        Assert.Equal(ReceiverModel._5A75E, model);
        Assert.Equal("5A75E", ReceiverModelParser.ToDisplayString(model));
    }

    #endregion

    #region TEST E: "8 tane E320 Pro" -> Count=8, Model=E320Pro

    [Fact]
    public void TestE_Extracts_Count_And_E320Pro_Model()
    {
        const string text = "8 tane E320 Pro";

        Assert.True(ReceiverCountParser.TryParse(text, out var count));
        Assert.Equal(8, count);

        Assert.True(ReceiverModelParser.TryParse(text, out var model));
        Assert.Equal(ReceiverModel.E320Pro, model);
        Assert.Equal("E320 Pro", ReceiverModelParser.ToDisplayString(model));
    }

    #endregion

    #region TEST F: "processor kullanıyorum" -> PlayerType=Processor, next missing fact is Processor HDMI

    [Fact]
    public void TestF_Extracts_Processor_And_Advances_To_Hdmi()
    {
        const string text = "processor kullanıyorum";

        Assert.True(PlayerTypeParser.TryParse(text, out var playerType));
        Assert.Equal(PlayerType.Processor, playerType);

        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
            ReceiverCount = 4,
            ReceiverModel = ReceiverModel.E120,
            PlayerType = playerType,
            ProcessorHdmiConnected = null,
        };

        var stage = WorkflowStageResolver.ResolveEffectiveStage(slots, configExampleAssetAvailable: true, configExampleStepCompleted: false);
        Assert.Equal(ConversationStage.AskProcessorHdmi, stage);

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = stage,
            StageLabel = "Processor HDMI",
            KnownFacts = ["Ekran: 320×160 cm", "Receiver: 4 adet E120", "Oynatıcı türü: Processor"],
            MissingFacts = ["Processor HDMI kablo bağlantısı", "Config bağlantı durumu", "AnyDesk adresi"],
            Player = new ContextPlayer("Processor", Complete: true, PlayerType.Processor),
            ProcessorHdmi = new ContextProcessorHdmi("HDMI bekleniyor", Complete: false),
            RecentMessages = []
        };

        var input = new HybridResponseComposeInput(
            context,
            slots,
            stage,
            new WorkflowInboundMessage(Guid.NewGuid(), "905551234567", Guid.NewGuid(), "ext-1", MessageType.Text, text),
            DeterministicClarifications: [],
            AiPlan: null,
            ConfigExampleAssetAvailable: true,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: null);

        var response = _composer.Compose(input);

        Assert.Single(response.OutboundTexts);
        Assert.Contains("HDMI", response.OutboundTexts[0]);
        Assert.Contains("bilgisayar", response.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("processor", response.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region TEST G: "playbox kullanıyorum" -> PlayerType=Playbox, do NOT ask Processor HDMI

    [Fact]
    public void TestG_Extracts_Playbox_And_Does_Not_Require_Processor_Hdmi()
    {
        const string text = "playbox kullanıyorum";

        Assert.True(PlayerTypeParser.TryParse(text, out var playerType));
        Assert.Equal(PlayerType.Playbox, playerType);

        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
            ReceiverCount = 4,
            ReceiverModel = ReceiverModel.E120,
            PlayerType = playerType,
            ProcessorHdmiConnected = null, // Playbox does not require processor HDMI
            ConfigConfirmed = false,
        };

        var stage = WorkflowStageResolver.ResolveEffectiveStage(slots, configExampleAssetAvailable: true, configExampleStepCompleted: false);
        // Advances directly to AskConfigConnection, skipping AskProcessorHdmi
        Assert.Equal(ConversationStage.AskConfigConnection, stage);

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = stage,
            StageLabel = "Config bağlantısı",
            KnownFacts = ["Ekran: 320×160 cm", "Receiver: 4 adet E120", "Oynatıcı türü: Playbox"],
            MissingFacts = ["Config bağlantı durumu", "AnyDesk adresi"],
            Player = new ContextPlayer("Playbox", Complete: true, PlayerType.Playbox),
            RecentMessages = []
        };

        var input = new HybridResponseComposeInput(
            context,
            slots,
            stage,
            new WorkflowInboundMessage(Guid.NewGuid(), "905551234567", Guid.NewGuid(), "ext-1", MessageType.Text, text),
            DeterministicClarifications: [],
            AiPlan: null,
            ConfigExampleAssetAvailable: true,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: null);

        var response = _composer.Compose(input);

        Assert.Single(response.OutboundTexts);
        Assert.Contains("config", response.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HDMI", response.OutboundTexts[0]);
    }

    #endregion

    #region TEST H: "processor var hdmi da bağlı" -> PlayerType=Processor, ProcessorHdmiConnected=true

    [Fact]
    public void TestH_Extracts_Processor_And_Hdmi_Confirmed()
    {
        const string text = "processor var hdmi da bağlı";

        Assert.True(PlayerTypeParser.TryParse(text, out var playerType));
        Assert.Equal(PlayerType.Processor, playerType);

        var hdmiResult = ProcessorHdmiParser.Parse(text);
        Assert.Equal(ProcessorHdmiParseResult.Confirmed, hdmiResult);

        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
            ReceiverCount = 4,
            ReceiverModel = ReceiverModel.E120,
            PlayerType = playerType,
            ProcessorHdmiConnected = true,
            ConfigConfirmed = false,
        };

        var stage = WorkflowStageResolver.ResolveEffectiveStage(slots, configExampleAssetAvailable: true, configExampleStepCompleted: false);
        // Skips AskProcessorHdmi and goes straight to AskConfigConnection
        Assert.Equal(ConversationStage.AskConfigConnection, stage);
    }

    #endregion

    #region TEST I: "4 tane E120 var processor kullanıyoruz hdmi da bağlı" -> All 4 extracted from ONE message

    [Fact]
    public void TestI_Extracts_All_Four_Facts_From_Single_Message()
    {
        const string text = "4 tane E120 var processor kullanıyoruz hdmi da bağlı";

        Assert.True(ReceiverCountParser.TryParse(text, out var count));
        Assert.Equal(4, count);

        Assert.True(ReceiverModelParser.TryParse(text, out var model));
        Assert.Equal(ReceiverModel.E120, model);

        Assert.True(PlayerTypeParser.TryParse(text, out var playerType));
        Assert.Equal(PlayerType.Processor, playerType);

        var hdmi = ProcessorHdmiParser.Parse(text);
        Assert.Equal(ProcessorHdmiParseResult.Confirmed, hdmi);

        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
            ReceiverCount = count,
            ReceiverModel = model,
            PlayerType = playerType,
            ProcessorHdmiConnected = true,
            ConfigConfirmed = false,
        };

        var stage = WorkflowStageResolver.ResolveEffectiveStage(slots, configExampleAssetAvailable: true, configExampleStepCompleted: false);
        Assert.Equal(ConversationStage.AskConfigConnection, stage);

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = stage,
            StageLabel = "Config bağlantısı",
            KnownFacts = ["Ekran: 320×160 cm", "Receiver: 4 adet E120", "Oynatıcı türü: Processor", "Processor HDMI bağlantısı yapıldı"],
            MissingFacts = ["Config bağlantı durumu", "AnyDesk adresi"],
            Receiver = new ContextReceiver("4 adet E120", Complete: true, CountComplete: true, ModelComplete: true, Count: 4, Model: ReceiverModel.E120),
            Player = new ContextPlayer("Processor", Complete: true, PlayerType.Processor),
            ProcessorHdmi = new ContextProcessorHdmi("HDMI bağlı", Complete: true, Connected: true),
            RecentMessages = []
        };

        var input = new HybridResponseComposeInput(
            context,
            slots,
            stage,
            new WorkflowInboundMessage(Guid.NewGuid(), "905551234567", Guid.NewGuid(), "ext-1", MessageType.Text, text),
            DeterministicClarifications: [],
            AiPlan: null,
            ConfigExampleAssetAvailable: true,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: null);

        var response = _composer.Compose(input);

        Assert.Single(response.OutboundTexts);
        Assert.Contains("config", response.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kaç adet", response.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("receiver", response.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("playbox", response.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hdmi", response.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region TEST J: "hdmi nereye bağlanacak?" -> Clarification, NOT classified as connected

    [Fact]
    public void TestJ_Hdmi_Clarification_Explains_Direction_And_Does_Not_Mark_Connected()
    {
        const string text = "hdmi nereye bağlanacak?";

        var result = ProcessorHdmiParser.Parse(text);
        Assert.Equal(ProcessorHdmiParseResult.NeedsClarification, result);

        Assert.Contains("çıkış", WorkflowPrompts.ExplainProcessorHdmi, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("giriş", WorkflowPrompts.ExplainProcessorHdmi, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region TEST K: Known facts guard prevents repeated receiver questions

    [Fact]
    public void TestK_Known_Facts_Never_ReAsked()
    {
        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
            ReceiverCount = 4,
            ReceiverModel = ReceiverModel.E120,
            PlayerType = PlayerType.Processor,
            ProcessorHdmiConnected = true,
            ConfigConfirmed = true,
        };

        var stage = WorkflowStageResolver.ResolveEffectiveStage(slots, configExampleAssetAvailable: true, configExampleStepCompleted: true);
        Assert.Equal(ConversationStage.AskAnyDesk, stage);

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = stage,
            StageLabel = "AnyDesk",
            KnownFacts = ["Ekran: 320×160 cm", "Panel etiket fotoğrafı alındı", "Receiver: 4 adet E120", "Oynatıcı türü: Processor", "Processor HDMI bağlantısı yapıldı", "Config bağlantısı onaylandı"],
            MissingFacts = ["AnyDesk adresi"],
            Screen = new ContextScreen("320 × 160 cm", Complete: true),
            Receiver = new ContextReceiver("4 adet E120", Complete: true, CountComplete: true, ModelComplete: true, Count: 4, Model: ReceiverModel.E120),
            Player = new ContextPlayer("Processor", Complete: true, PlayerType.Processor),
            ProcessorHdmi = new ContextProcessorHdmi("HDMI bağlı", Complete: true, Connected: true),
            Config = new ContextConfig("Config bağlantısı tamam", Complete: true),
            AnyDesk = new ContextAnyDesk("AnyDesk bekleniyor", Complete: false),
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
            ConfigExampleCompleted: true,
            ConfigExampleImagePath: null);

        var response = _composer.Compose(input);

        Assert.Single(response.OutboundTexts);
        Assert.Contains("AnyDesk", response.OutboundTexts[0]);
        Assert.DoesNotContain("receiver", response.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("playbox", response.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hdmi", response.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("config", response.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ekran", response.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Corrections & Unknown Model Handling

    [Fact]
    public void Model_Correction_Updates_Model_And_Preserves_Count()
    {
        const string text = "pardon E120 değil E80";
        Assert.True(ReceiverModelParser.TryParse(text, out var model));
        Assert.Equal(ReceiverModel.E80, model);
    }

    [Fact]
    public void Count_Correction_Updates_Count()
    {
        const string text = "4 değil 6 tane var";
        Assert.True(ReceiverCountParser.TryParse(text, out var count));
        Assert.Equal(6, count);
    }

    [Fact]
    public void Unknown_Receiver_Model_Does_Not_Force_Supported_Model()
    {
        const string text = "X100 receiver kullanıyorum";
        Assert.False(ReceiverModelParser.TryParse(text, out var model));
        Assert.Equal(ReceiverModel.Unknown, model);
    }

    [Fact]
    public void USB_Config_And_Processor_HDMI_Are_Strictly_Separated()
    {
        // HDMI confirmation should not confirm config
        const string hdmiMsg = "hdmi kablosunu bağladım";
        Assert.Equal(ProcessorHdmiParseResult.Confirmed, ProcessorHdmiParser.Parse(hdmiMsg));
        Assert.NotEqual(ConfigConfirmationResult.Confirmed, ConfigConfirmationParser.Parse(hdmiMsg));

        // Config confirmation should not confirm HDMI
        const string configMsg = "yazıcı kablosunu bağladım";
        Assert.Equal(ConfigConfirmationResult.Confirmed, ConfigConfirmationParser.Parse(configMsg));
        Assert.Equal(ProcessorHdmiParseResult.None, ProcessorHdmiParser.Parse(configMsg));
    }

    #endregion

    #region AI Extraction Validator Tests

    [Theory]
    [InlineData("E120", ReceiverModel.E120)]
    [InlineData("E80", ReceiverModel.E80)]
    [InlineData("5A75E", ReceiverModel._5A75E)]
    [InlineData("E320 Pro", ReceiverModel.E320Pro)]
    [InlineData("e320pro", ReceiverModel.E320Pro)]
    public void AiExtractionValidator_Validates_All_Four_Receiver_Models(string modelText, ReceiverModel expectedModel)
    {
        var validator = new AiExtractionValidator(Options.Create(new AiExtractionOptions { ConfidenceThreshold = 0.5 }));
        var extraction = new AiStructuredExtraction(
            Intent: ConversationIntent.WorkflowInformation,
            Screen: null,
            Receiver: new ReceiverSlotCandidate(4, modelText, 0.95),
            Player: null,
            ProcessorHdmi: null,
            Config: null,
            AnyDesk: null,
            Ambiguities: []);

        var validated = validator.Validate(extraction);

        Assert.Equal(4, validated.ReceiverCount);
        Assert.Equal(expectedModel, validated.ReceiverModel);
    }

    [Theory]
    [InlineData(PlayerType.Playbox)]
    [InlineData(PlayerType.Processor)]
    public void AiExtractionValidator_Validates_Player_Types(PlayerType expectedPlayer)
    {
        var validator = new AiExtractionValidator(Options.Create(new AiExtractionOptions { ConfidenceThreshold = 0.5 }));
        var extraction = new AiStructuredExtraction(
            Intent: ConversationIntent.WorkflowInformation,
            Screen: null,
            Receiver: null,
            Player: new PlayerSlotCandidate(expectedPlayer, 0.95),
            ProcessorHdmi: null,
            Config: null,
            AnyDesk: null,
            Ambiguities: []);

        var validated = validator.Validate(extraction);

        Assert.Equal(expectedPlayer, validated.PlayerType);
    }

    [Fact]
    public void AiExtractionValidator_Validates_Processor_Hdmi()
    {
        var validator = new AiExtractionValidator(Options.Create(new AiExtractionOptions { ConfidenceThreshold = 0.5 }));
        var extraction = new AiStructuredExtraction(
            Intent: ConversationIntent.WorkflowInformation,
            Screen: null,
            Receiver: null,
            Player: new PlayerSlotCandidate(PlayerType.Processor, 0.95),
            ProcessorHdmi: new ProcessorHdmiSlotCandidate(Connected: true, Confidence: 0.95, NeedsClarification: false),
            Config: null,
            AnyDesk: null,
            Ambiguities: []);

        var validated = validator.Validate(extraction);

        Assert.True(validated.ProcessorHdmiConnected);
        Assert.False(validated.ProcessorHdmiNeedsClarification);
    }

    #endregion

    #region Typo and Variant Robustness Tests

    [Theory]
    [InlineData("e 120", ReceiverModel.E120)]
    [InlineData("E 80", ReceiverModel.E80)]
    [InlineData("5a 75e", ReceiverModel._5A75E)]
    [InlineData("5a-75e", ReceiverModel._5A75E)]
    [InlineData("e 320 pro", ReceiverModel.E320Pro)]
    [InlineData("e320", ReceiverModel.E320Pro)]
    public void ReceiverModelParser_Handles_Typo_And_Spacing_Variants(string input, ReceiverModel expected)
    {
        Assert.True(ReceiverModelParser.TryParse(input, out var model));
        Assert.Equal(expected, model);
    }

    [Theory]
    [InlineData("play box", PlayerType.Playbox)]
    [InlineData("playbox", PlayerType.Playbox)]
    [InlineData("pleybox", PlayerType.Playbox)]
    [InlineData("processor", PlayerType.Processor)]
    [InlineData("prosesör", PlayerType.Processor)]
    [InlineData("prossesor", PlayerType.Processor)]
    [InlineData("prossesör", PlayerType.Processor)]
    [InlineData("işlemci", PlayerType.Processor)]
    public void PlayerTypeParser_Handles_Natural_Variants(string input, PlayerType expected)
    {
        Assert.True(PlayerTypeParser.TryParse(input, out var player));
        Assert.Equal(expected, player);
    }

    [Theory]
    [InlineData("hdmi nereye takacağım?")]
    [InlineData("hangi hdmi?")]
    [InlineData("anlamadım")]
    [InlineData("bilgisayardan nereye gidecek?")]
    [InlineData("nereye bağlayacağım?")]
    public void ProcessorHdmiParser_Identifies_Clarification_Requests(string text)
    {
        Assert.Equal(ProcessorHdmiParseResult.NeedsClarification, ProcessorHdmiParser.Parse(text));
    }

    [Theory]
    [InlineData("bağladım")]
    [InlineData("evet bağlı")]
    [InlineData("bağlı zaten")]
    [InlineData("hdmi da bağlı")]
    [InlineData("hdmi bağladım")]
    public void ProcessorHdmiParser_Identifies_Confirmations(string text)
    {
        Assert.Equal(ProcessorHdmiParseResult.Confirmed, ProcessorHdmiParser.Parse(text));
    }

    #endregion
}
