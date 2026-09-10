using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Admin;
using LedSupBot.Application.Workflow;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Application.Workflow.Knowledge;
using LedSupBot.Application.Workflow.Parsing;
using LedSupBot.Application.WhatsApp;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LedSupBot.UnitTests;

public sealed class GeminiGeneralTechnicalSupportTests
{
    private readonly HybridResponseComposer _composer = new(NullLogger<HybridResponseComposer>.Instance);
    private readonly ITechnicalKnowledgeProvider _knowledgeProvider = new DefaultTechnicalKnowledgeProvider();

    [Fact]
    public async Task TestA_General_Technical_Question_Answered_Naturally_No_Blind_Workflow_Loop()
    {
        // Customer: "Ekranın sağ tarafı pembe çıkıyor, neden olabilir?"
        const string customerMessage = "Ekranın sağ tarafı pembe çıkıyor, neden olabilir?";

        var knowledge = await _knowledgeProvider.GetRelevantKnowledgeAsync(
            customerMessage,
            []);

        Assert.NotNull(knowledge);
        Assert.Contains("Renk bozulması", knowledge);

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.Start,
            StageLabel = "Başlangıç",
            KnownFacts = [],
            MissingFacts = ["Ekran ölçüsü", "Panel etiket fotoğrafı", "Receiver modeli ve adedi", "Oynatıcı türü (Playbox / Processor)", "Config bağlantı durumu"],
            TechnicalKnowledgeContext = knowledge,
            RecentMessages = []
        };

        var slots = new WorkflowSlotState();

        var aiPlan = new ValidatedAiResponsePlan(
            Intent: ConversationIntent.LedTechnicalQuestion,
            Reply: "Sağ taraftaki renk bozulması genellikle ilk problemli modüle giren yassı data kablosundan veya receiver çıkış portundan kaynaklanır. Sorunun başladığı ilk paneli biliyor musunuz?",
            IssueSummary: "Sağ tarafta pembe renk bozulması",
            NeedsHuman: false,
            SuggestedNextAction: "check_first_problematic_panel",
            AiOperatorSummary: "Müşteri ekranın sağ tarafındaki pembeleşmeyi sordu. Flat data kablosu ve receiver port kontrolü önerildi.",
            CustomerQuestion: customerMessage,
            ShouldAnswerQuestion: true,
            ShouldContinueWorkflowNow: false,
            AnswerConfidence: 0.9);

        var input = new HybridResponseComposeInput(
            context,
            slots,
            ConversationStage.Start,
            new WorkflowInboundMessage(Guid.NewGuid(), "905425192119", Guid.NewGuid(), "msg-a", MessageType.Text, customerMessage, null),
            DeterministicClarifications: [],
            AiPlan: aiPlan,
            ConfigExampleAssetAvailable: false,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: null);

        var result = _composer.Compose(input);

        // Assert: Intent classified as general_technical_question, natural reply produced, no workflow questionnaire forced
        Assert.True(aiPlan.IsGeneralTechnicalQuestion);
        Assert.Single(result.OutboundTexts);
        Assert.Contains("renk bozulması", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ölçüleriniz nedir", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kaç cm", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kaç adet receiver", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TestB_Workflow_Interruption_Answers_Question_First_Without_Immediate_Workflow_Repetition()
    {
        // Current missing: ReceiverCount. Bot previously asked for receiver count.
        // Customer: "Ekranın yarısı gelmiyor neden?"
        const string customerMessage = "Ekranın yarısı gelmiyor neden?";

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.AskReceiverCount,
            StageLabel = "Receiver Bilgisi",
            LastBotQuestion = WorkflowPrompts.AskReceiverCount,
            KnownFacts = ["Ekran: 320×160 cm", "Panel etiket fotoğrafı alındı"],
            MissingFacts = ["Receiver adedi", "Receiver modeli"],
            Screen = new ContextScreen("320 × 160 cm", Complete: true),
            Panel = new ContextPanel("Panel etiketi alındı", Complete: true),
            RecentMessages =
            [
                new ContextMessage("bot", WorkflowPrompts.AskReceiverCount, DateTimeOffset.UtcNow.AddMinutes(-1))
            ]
        };

        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
        };

        var aiPlan = new ValidatedAiResponsePlan(
            Intent: ConversationIntent.LedTechnicalQuestion,
            Reply: "Ekranın yarısının gelmemesi data kablosu, receiver beslemesi veya port kapasitesiyle ilgili olabilir. Görüntünün kesildiği ilk paneli biliyor musunuz?",
            IssueSummary: "Ekranın yarısında görüntü yok",
            NeedsHuman: false,
            SuggestedNextAction: "check_signal_chain",
            AiOperatorSummary: "Müşteri kurulum sırasında ekranın yarısının gelmediğini sordu. Sinyal zincirinin kesildiği panel tespiti istendi.",
            CustomerQuestion: customerMessage,
            ShouldAnswerQuestion: true,
            ShouldContinueWorkflowNow: false,
            AnswerConfidence: 0.88);

        var input = new HybridResponseComposeInput(
            context,
            slots,
            ConversationStage.AskReceiverCount,
            new WorkflowInboundMessage(Guid.NewGuid(), "905425192119", Guid.NewGuid(), "msg-b", MessageType.Text, customerMessage, null),
            DeterministicClarifications: [],
            AiPlan: aiPlan,
            ConfigExampleAssetAvailable: false,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: null);

        var result = _composer.Compose(input);

        // Assert: Technical question answered first; receiver count question NOT immediately repeated
        Assert.Single(result.OutboundTexts);
        Assert.Contains("data kablosu", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(WorkflowPrompts.AskReceiverCount, result.OutboundTexts);
    }

    [Fact]
    public void TestC_Question_Plus_Facts_Extracts_Facts_And_Answers_Technical_Question()
    {
        // Customer: "4 tane E120 var ama ikinci receiverdan sonra görüntü yok"
        const string customerMessage = "4 tane E120 var ama ikinci receiverdan sonra görüntü yok";

        // 1. Assert fact extraction
        Assert.True(ReceiverCountParser.TryParse(customerMessage, out var count));
        Assert.Equal(4, count);

        Assert.True(ReceiverModelParser.TryParse(customerMessage, out var model));
        Assert.Equal(ReceiverModel.E120, model);

        // 2. Response planning
        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.AskPlayerType,
            StageLabel = "Oynatıcı Türü",
            KnownFacts = ["Ekran: 500×300 cm", "Receiver: 4 adet E120"],
            MissingFacts = ["Oynatıcı türü (Playbox / Processor)", "Config bağlantı durumu"],
            Receiver = new ContextReceiver("4 adet E120", Complete: true, CountComplete: true, ModelComplete: true, Count: 4, Model: ReceiverModel.E120),
            RecentMessages = []
        };

        var slots = new WorkflowSlotState
        {
            WidthCm = 500,
            HeightCm = 300,
            HasPanelLabelPhoto = true,
            ReceiverCount = 4,
            ReceiverModel = ReceiverModel.E120,
        };

        var aiPlan = new ValidatedAiResponsePlan(
            Intent: ConversationIntent.LedTechnicalQuestion,
            Reply: "4 adet E120 receiver bilginizi kaydettim. 2. receiverdan sonra görüntünün kesilmesi 2. ve 3. receiver arasındaki RJ45 data kablosundan veya 3. receiverın 5V beslemesinden kaynaklanabilir. 3. receiver üzerindeki güç LED'i yanıyor mu?",
            IssueSummary: "İkinci receiverdan sonra görüntü kesilmesi",
            NeedsHuman: false,
            SuggestedNextAction: "check_receiver_2_3_cable_and_power",
            AiOperatorSummary: "4 adet E120 receiver kullanılıyor. 2. receiverdan sonraki sinyal kesintisi için ara data kablosu ve 3. kart besleme kontrolü önerildi.",
            CustomerQuestion: customerMessage,
            ShouldAnswerQuestion: true,
            ShouldContinueWorkflowNow: false,
            AnswerConfidence: 0.92);

        var input = new HybridResponseComposeInput(
            context,
            slots,
            ConversationStage.AskPlayerType,
            new WorkflowInboundMessage(Guid.NewGuid(), "905425192119", Guid.NewGuid(), "msg-c", MessageType.Text, customerMessage, null),
            DeterministicClarifications: [],
            AiPlan: aiPlan,
            ConfigExampleAssetAvailable: false,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: null);

        var result = _composer.Compose(input);

        // Assert: Facts extracted, issue merged, Gemini answers troubleshooting
        Assert.Single(result.OutboundTexts);
        Assert.Contains("E120", result.OutboundTexts[0]);
        Assert.Contains("data kablosu", result.OutboundTexts[0]);
        Assert.DoesNotContain("kaç adet receiver", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("receiver modeli hangisi", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TestD_Multi_Intent_Extracts_PlayerType_And_Troubleshoots_Hdmi_No_Image()
    {
        // Customer: "processor bağlı HDMI'dan görüntü alamıyorum"
        const string customerMessage = "processor bağlı HDMI'dan görüntü alamıyorum";

        Assert.True(PlayerTypeParser.TryParse(customerMessage, out var playerType));
        Assert.Equal(PlayerType.Processor, playerType);

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.AskProcessorHdmi,
            StageLabel = "Processor HDMI Bağlantısı",
            KnownFacts = ["Ekran: 320×160 cm", "Receiver: 4 adet E120", "Oynatıcı türü: Processor"],
            MissingFacts = ["Processor HDMI kablo bağlantısı", "Config bağlantı durumu"],
            Player = new ContextPlayer("Processor", Complete: true, PlayerType.Processor),
            RecentMessages = []
        };

        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
            ReceiverCount = 4,
            ReceiverModel = ReceiverModel.E120,
            PlayerType = PlayerType.Processor,
        };

        var aiPlan = new ValidatedAiResponsePlan(
            Intent: ConversationIntent.LedTechnicalQuestion,
            Reply: "Processor kullandığınızı kaydettim. HDMI görüntüsü gelmiyorsa: 1) Bilgisayarda ekranı 'Yinele/Genişlet' moduna alın (Win+P). 2) Processor ön panelindeki HDMI giriş butonuna basarak kaynağın aktif olduğunu doğrulayın. Bilgisayar görüntüsü yansıyor mu?",
            IssueSummary: "Processor HDMI görüntüsü gelmiyor",
            NeedsHuman: false,
            SuggestedNextAction: "check_hdmi_source_and_display_mode",
            AiOperatorSummary: "Oynatıcı: Processor. HDMI'dan görüntü alınamıyor. Win+P ekran modu ve ön panel HDMI kaynak kontrolü önerildi.",
            CustomerQuestion: customerMessage,
            ShouldAnswerQuestion: true,
            ShouldContinueWorkflowNow: false,
            AnswerConfidence: 0.94);

        var input = new HybridResponseComposeInput(
            context,
            slots,
            ConversationStage.AskProcessorHdmi,
            new WorkflowInboundMessage(Guid.NewGuid(), "905425192119", Guid.NewGuid(), "msg-d", MessageType.Text, customerMessage, null),
            DeterministicClarifications: [],
            AiPlan: aiPlan,
            ConfigExampleAssetAvailable: false,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: null);

        var result = _composer.Compose(input);

        Assert.Single(result.OutboundTexts);
        Assert.Contains("HDMI", result.OutboundTexts[0]);
        Assert.Contains("Processor", result.OutboundTexts[0]);
        Assert.DoesNotContain("Playbox mı kullanıyorsunuz", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TestE_Current_Step_Clarification_Explains_Requested_Information_Helpfully()
    {
        // Bot asked receiver model/count. Customer: "receiver kart hangisi?"
        const string customerMessage = "receiver kart hangisi?";

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.AskReceiverCount,
            StageLabel = "Receiver Bilgisi",
            LastBotQuestion = WorkflowPrompts.AskReceiverCount,
            KnownFacts = ["Ekran: 320×160 cm", "Panel etiket fotoğrafı alındı"],
            MissingFacts = ["Receiver adedi", "Receiver modeli"],
            RecentMessages =
            [
                new ContextMessage("bot", WorkflowPrompts.AskReceiverCount, DateTimeOffset.UtcNow.AddMinutes(-1))
            ]
        };

        var slots = new WorkflowSlotState
        {
            WidthCm = 320,
            HeightCm = 160,
            HasPanelLabelPhoto = true,
        };

        var aiPlan = new ValidatedAiResponsePlan(
            Intent: ConversationIntent.QuestionAboutCurrentStep,
            Reply: "Panel arkasındaki data kablolarının bağlandığı yeşil receiver (alıcı) karttan bahsediyorum. Kartın üzerinde model olarak E120, E80 veya 5A75E yazabilir. Panel arkasında bundan kaç adet var?",
            IssueSummary: null,
            NeedsHuman: false,
            SuggestedNextAction: "explain_receiver_card",
            AiOperatorSummary: "Müşteri receiver kartının ne olduğunu sordu. Panel arkasındaki yeşil kontrol kartı açıklandı.",
            CustomerQuestion: customerMessage,
            ShouldAnswerQuestion: true,
            ShouldContinueWorkflowNow: true,
            AnswerConfidence: 0.95);

        var input = new HybridResponseComposeInput(
            context,
            slots,
            ConversationStage.AskReceiverCount,
            new WorkflowInboundMessage(Guid.NewGuid(), "905425192119", Guid.NewGuid(), "msg-e", MessageType.Text, customerMessage, null),
            DeterministicClarifications: [],
            AiPlan: aiPlan,
            ConfigExampleAssetAvailable: false,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: null);

        var result = _composer.Compose(input);

        Assert.True(aiPlan.IsStepClarification);
        Assert.Single(result.OutboundTexts);
        Assert.Contains("yeşil receiver", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TestF_Completed_Case_Question_Answers_Naturally_Without_Rigid_Questionnaire()
    {
        // After case completed, customer asks: "Bir şey soracağım, ekran titriyor"
        const string customerMessage = "Bir şey soracağım, ekran titriyor";

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.Start,
            StageLabel = "Başlangıç",
            CompletedCasesCount = 1,
            PreviousCasesSummary = "[Tamamlandı: 320x160 cm LED ekran kurulumu]",
            KnownFacts = ["Önceki 1 adet teknik destek kaydı başarıyla tamamlandı."],
            MissingFacts = [],
            RecentMessages = []
        };

        var slots = new WorkflowSlotState();

        var aiPlan = new ValidatedAiResponsePlan(
            Intent: ConversationIntent.LedTechnicalQuestion,
            Reply: "Ekran titremesi yenileme hızı (refresh rate), 5V güç kaynağı voltaj dalgalanması veya topraklama eksikliğinden kaynaklanabilir. Titreme ekranın tamamında mı yoksa belirli bir bölgede mi görülüyor?",
            IssueSummary: "Ekran titremesi",
            NeedsHuman: false,
            SuggestedNextAction: "troubleshoot_flickering",
            AiOperatorSummary: "Müşteri tamamlanan kaydın ardından ekran titremesi sordu. Güç kaynağı ve yenileme hızı kontrolü önerildi.",
            CustomerQuestion: customerMessage,
            ShouldAnswerQuestion: true,
            ShouldContinueWorkflowNow: false,
            AnswerConfidence: 0.91);

        var input = new HybridResponseComposeInput(
            context,
            slots,
            ConversationStage.Start,
            new WorkflowInboundMessage(Guid.NewGuid(), "905425192119", Guid.NewGuid(), "msg-f", MessageType.Text, customerMessage, null),
            DeterministicClarifications: [],
            AiPlan: aiPlan,
            ConfigExampleAssetAvailable: false,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: null);

        var result = _composer.Compose(input);

        Assert.Single(result.OutboundTexts);
        Assert.Contains("titreme", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ekran ölçü", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kaç cm", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TestG_Casual_Acknowledgement_Short_Natural_Reply_No_Questionnaire()
    {
        // Customer: "teşekkür ederim"
        const string customerMessage = "teşekkür ederim";

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.Start,
            StageLabel = "Başlangıç",
            CompletedCasesCount = 1,
            PreviousCasesSummary = "[Tamamlandı: LED Destek]",
            KnownFacts = [],
            MissingFacts = [],
            RecentMessages = []
        };

        var slots = new WorkflowSlotState();

        var aiPlan = new ValidatedAiResponsePlan(
            Intent: ConversationIntent.Acknowledgement,
            Reply: "Rica ederim, iyi çalışmalar dilerim. Başka bir sorunuz veya ihtiyacınız olursa buradayım.",
            IssueSummary: null,
            NeedsHuman: false,
            SuggestedNextAction: null,
            AiOperatorSummary: "Müşteri teşekkür etti.");

        var input = new HybridResponseComposeInput(
            context,
            slots,
            ConversationStage.Start,
            new WorkflowInboundMessage(Guid.NewGuid(), "905425192119", Guid.NewGuid(), "msg-g", MessageType.Text, customerMessage, null),
            DeterministicClarifications: [],
            AiPlan: aiPlan,
            ConfigExampleAssetAvailable: false,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: null);

        var result = _composer.Compose(input);

        Assert.True(aiPlan.IsCasualOrAcknowledgement);
        Assert.Single(result.OutboundTexts);
        Assert.Contains("Rica ederim", result.OutboundTexts[0]);
        Assert.DoesNotContain("ekran ölçü", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestH_Known_Fact_Usage_Tailors_Answer_To_E120_Without_ReAsking_Model()
    {
        // Known: ReceiverModel = E120
        // Customer: "receiver neden online olmuyor?"
        const string customerMessage = "receiver neden online olmuyor?";

        var knownFacts = new List<string> { "Receiver: 4 adet E120" };
        var knowledge = await _knowledgeProvider.GetRelevantKnowledgeAsync(customerMessage, knownFacts);

        Assert.NotNull(knowledge);
        Assert.Contains("E120", knowledge);

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.AskConfigConnection,
            StageLabel = "Config Bağlantısı",
            KnownFacts = knownFacts,
            MissingFacts = ["Config bağlantı durumu"],
            Receiver = new ContextReceiver("4 adet E120", Complete: true, CountComplete: true, ModelComplete: true, Count: 4, Model: ReceiverModel.E120),
            TechnicalKnowledgeContext = knowledge,
            RecentMessages = []
        };

        var slots = new WorkflowSlotState
        {
            ReceiverCount = 4,
            ReceiverModel = ReceiverModel.E120,
        };

        var aiPlan = new ValidatedAiResponsePlan(
            Intent: ConversationIntent.LedTechnicalQuestion,
            Reply: "E120 receiver kullandığınızı görüyorum. Kartın yazılımda online görünmemesi durumunda: 1) E120 üzerindeki 5V kırmızı ve yeşil durum LED'leri yanıyor mu? 2) Bilgisayar ile cihaz arasındaki USB config kablosu ve gigabit data kablosu takılı mı kontrol edelim.",
            IssueSummary: "E120 receiver online görünmüyor",
            NeedsHuman: false,
            SuggestedNextAction: "check_e120_power_and_usb_data",
            AiOperatorSummary: "Müşteri E120 receiver kartının online olmamasını sordu. 5V güç led ve USB config bağlantı kontrolü önerildi.",
            CustomerQuestion: customerMessage,
            ShouldAnswerQuestion: true,
            ShouldContinueWorkflowNow: false,
            AnswerConfidence: 0.93);

        var input = new HybridResponseComposeInput(
            context,
            slots,
            ConversationStage.AskConfigConnection,
            new WorkflowInboundMessage(Guid.NewGuid(), "905425192119", Guid.NewGuid(), "msg-h", MessageType.Text, customerMessage, null),
            DeterministicClarifications: [],
            AiPlan: aiPlan,
            ConfigExampleAssetAvailable: false,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: null);

        var result = _composer.Compose(input);

        Assert.Single(result.OutboundTexts);
        Assert.Contains("E120", result.OutboundTexts[0]);
        Assert.DoesNotContain("receiver modeli hangisi", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hangi receiver", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TestI_Group_And_Excluded_Identity_Safety_Blocks_General_Questions()
    {
        var allowlist = new WhatsAppTestAllowlist(
            Options.Create(new WhatsAppTestModeOptions { Enabled = false }),
            Options.Create(new WhatsAppAutomationOptions { ExcludedPhoneNumbers = ["+90 506 336 09 19"] }),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WhatsAppTestAllowlist>.Instance);

        // 1. Group inbound asking technical question
        var groupInbound = new WhatsAppInboundEvent
        {
            RemoteJid = "123456789-987654@g.us",
            TextContent = "Ekranın sağ tarafı pembe çıkıyor, neden olabilir?",
            FromMe = false
        };
        Assert.False(allowlist.IsAutoReplyAllowed(groupInbound, "123456789-987654@g.us", null));

        // 2. Excluded customer asking technical question
        var excludedInbound = new WhatsAppInboundEvent
        {
            RemotePhoneNumber = "905063360919",
            TextContent = "Ekranın sağ tarafı pembe çıkıyor, neden olabilir?",
            FromMe = false
        };
        Assert.False(allowlist.IsAutoReplyAllowed(excludedInbound, "905063360919", null));
        Assert.False(allowlist.IsOutboundAllowed("905063360919@s.whatsapp.net", null));
    }
}
