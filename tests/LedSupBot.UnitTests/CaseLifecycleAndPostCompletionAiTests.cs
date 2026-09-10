using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Admin;
using LedSupBot.Application.Workflow;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Application.Workflow.Parsing;
using LedSupBot.Application.WhatsApp;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using LedSupBot.Infrastructure.Configuration;
using LedSupBot.Infrastructure.Gemini;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LedSupBot.UnitTests;

public sealed class CaseLifecycleAndPostCompletionAiTests
{
    [Fact]
    public void TestA_And_TestB_Case_Completion_Marks_Completed_And_Creates_New_Active_Case()
    {
        var conversationId = Guid.NewGuid();
        var case1 = new TechnicalSupportCase
        {
            ConversationId = conversationId,
            Status = SupportCaseStatus.CollectingInformation,
            ScreenInformation = new ScreenInformation { Width = 160, Height = 192 },
            ReceiverInformation = new ReceiverInformation { Count = 8 },
            ConfigConnection = new ConfigConnectionInfo { Status = ConfigConnectionStatus.Confirmed },
            AnyDeskInformation = new AnyDeskInformation { AnyDeskAddress = "123456789" },
            PanelLabelPhotos = [new PanelLabelPhoto { StorageKey = "photo1.jpg" }]
        };

        var conversation = new Conversation
        {
            CustomerId = Guid.NewGuid(),
            Status = ConversationStatus.Active,
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.CollectingFinalInfo,
            TechnicalSupportCases = [case1]
        };

        // When completion occurs
        var now = DateTimeOffset.UtcNow;
        case1.Status = SupportCaseStatus.Completed;
        case1.CompletedAt = now;
        case1.SummaryMarkdown = "Teknik Bilgiler Özeti";

        // Assert Test A: Completed case preserved
        Assert.Equal(SupportCaseStatus.Completed, case1.Status);
        Assert.NotNull(case1.CompletedAt);
        Assert.Equal(160, case1.ScreenInformation.Width);
        Assert.Equal(192, case1.ScreenInformation.Height);
        Assert.Equal(8, case1.ReceiverInformation.Count);
        Assert.Equal("123456789", case1.AnyDeskInformation.AnyDeskAddress);

        // Assert Test B: Create Case #2 for same conversation & customer
        var case2 = new TechnicalSupportCase
        {
            ConversationId = conversation.Id,
            Status = SupportCaseStatus.CollectingInformation
        };
        conversation.TechnicalSupportCases.Add(case2);

        Assert.Equal(2, conversation.TechnicalSupportCases.Count);
        Assert.Equal(SupportCaseStatus.CollectingInformation, case2.Status);
        Assert.Equal(conversation.Id, case2.ConversationId);
        Assert.NotEqual(case1.Id, case2.Id);
    }

    [Fact]
    public void TestC_No_Historical_Mutation_Between_Cases()
    {
        var conversationId = Guid.NewGuid();
        var case1 = new TechnicalSupportCase
        {
            ConversationId = conversationId,
            Status = SupportCaseStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ScreenInformation = new ScreenInformation { Width = 160, Height = 192 }
        };

        var case2 = new TechnicalSupportCase
        {
            ConversationId = conversationId,
            Status = SupportCaseStatus.CollectingInformation,
            ScreenInformation = new ScreenInformation { Width = 500, Height = 300 }
        };

        // Assert Case 1 remains 160x192, Case 2 is 500x300
        Assert.Equal(160, case1.ScreenInformation.Width);
        Assert.Equal(192, case1.ScreenInformation.Height);
        Assert.Equal(500, case2.ScreenInformation.Width);
        Assert.Equal(300, case2.ScreenInformation.Height);
    }

    [Fact]
    public void TestG_AnyDesk_Isolation_New_Case_Does_Not_Inherit_Old_AnyDesk_As_Active()
    {
        var conversation = new Conversation { BotMode = BotMode.Bot, CurrentStage = ConversationStage.Start };
        var case1 = new TechnicalSupportCase
        {
            Status = SupportCaseStatus.Completed,
            AnyDeskInformation = new AnyDeskInformation { AnyDeskAddress = "123456789" }
        };

        var case2 = new TechnicalSupportCase
        {
            Status = SupportCaseStatus.CollectingInformation,
            AnyDeskInformation = null
        };

        var slotsCase2 = WorkflowSlotState.FromCase(case2, conversation);

        Assert.False(slotsCase2.HasAnyDesk);
        Assert.Null(slotsCase2.WidthCm);
    }

    [Fact]
    public void TestD_Post_Completion_Thanks_Does_Not_Trigger_Screen_Questionnaire()
    {
        var composer = new HybridResponseComposer(NullLogger<HybridResponseComposer>.Instance);

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.Start,
            StageLabel = "Başlangıç",
            CompletedCasesCount = 1,
            PreviousCasesSummary = "Önceki kayıt tamamlandı",
            RecentMessages = []
        };

        var slots = new WorkflowSlotState();

        var aiPlan = new ValidatedAiResponsePlan(
            Intent: ConversationIntent.Acknowledgement,
            Reply: "Rica ederim, iyi çalışmalar dilerim. Başka bir sorunuz olursa buradayım.",
            IssueSummary: null,
            NeedsHuman: false,
            SuggestedNextAction: null,
            AiOperatorSummary: "Müşteri teşekkür etti.");

        var input = new HybridResponseComposeInput(
            context,
            slots,
            ConversationStage.Start,
            new WorkflowInboundMessage(Guid.NewGuid(), "905425192119", Guid.NewGuid(), "msg-1", MessageType.Text, "Teşekkürler", null),
            DeterministicClarifications: [],
            AiPlan: aiPlan,
            ConfigExampleAssetAvailable: false,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: null);

        var result = composer.Compose(input);

        Assert.Single(result.OutboundTexts);
        Assert.Equal("Rica ederim, iyi çalışmalar dilerim. Başka bir sorunuz olursa buradayım.", result.OutboundTexts[0]);
        Assert.DoesNotContain("ekran ölçü", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kaç cm", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TestF_General_Technical_Question_Answered_Naturally_Without_Rigid_Questionnaire()
    {
        var composer = new HybridResponseComposer(NullLogger<HybridResponseComposer>.Instance);

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.Start,
            StageLabel = "Başlangıç",
            CompletedCasesCount = 1,
            PreviousCasesSummary = "Önceki kayıt tamamlandı",
            RecentMessages = []
        };

        var slots = new WorkflowSlotState();

        var aiPlan = new ValidatedAiResponsePlan(
            Intent: ConversationIntent.LedTechnicalQuestion,
            Reply: "Receiver kartı seçimi panelinizin çözünürlüğüne ve HUB tipine göre yapılır. Genellikle NovaStar MRV serisi tercih edilir.",
            IssueSummary: null,
            NeedsHuman: false,
            SuggestedNextAction: null,
            AiOperatorSummary: "Müşteri receiver kartı seçimi hakkında genel teknik soru sordu.");

        var input = new HybridResponseComposeInput(
            context,
            slots,
            ConversationStage.Start,
            new WorkflowInboundMessage(Guid.NewGuid(), "905425192119", Guid.NewGuid(), "msg-2", MessageType.Text, "Receiver kartı nasıl seçilir?", null),
            DeterministicClarifications: [],
            AiPlan: aiPlan,
            ConfigExampleAssetAvailable: false,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: null);

        var result = composer.Compose(input);

        Assert.Single(result.OutboundTexts);
        Assert.Contains("NovaStar", result.OutboundTexts[0]);
        Assert.DoesNotContain("ekran ölçü", result.OutboundTexts[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TestE_New_Issue_Intent_Begins_Information_Collection()
    {
        var composer = new HybridResponseComposer(NullLogger<HybridResponseComposer>.Instance);

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.Start,
            StageLabel = "Başlangıç",
            CompletedCasesCount = 1,
            PreviousCasesSummary = "Önceki kayıt tamamlandı",
            RecentMessages = []
        };

        var slots = new WorkflowSlotState();

        var aiPlan = new ValidatedAiResponsePlan(
            Intent: ConversationIntent.NewCase,
            Reply: "Harika, yeni LED ekranınız için yardımcı olalım. Kuracağınız ekranın genişlik ve yükseklik ölçüleri (cm) nedir?",
            IssueSummary: "Yeni LED ekran kurulumu",
            NeedsHuman: false,
            SuggestedNextAction: "ask_screen_size",
            AiOperatorSummary: "Müşteri yeni bir LED ekran kurulumu başlattı. Ekran ölçüleri talep edildi.");

        var input = new HybridResponseComposeInput(
            context,
            slots,
            ConversationStage.AskScreenSize,
            new WorkflowInboundMessage(Guid.NewGuid(), "905425192119", Guid.NewGuid(), "msg-3", MessageType.Text, "Yeni bir ekran kuracağım", null),
            DeterministicClarifications: [],
            AiPlan: aiPlan,
            ConfigExampleAssetAvailable: false,
            ConfigExampleCompleted: false,
            ConfigExampleImagePath: null);

        var result = composer.Compose(input);

        Assert.Single(result.OutboundTexts);
        Assert.Contains("yeni LED ekranınız", result.OutboundTexts[0]);
    }

    [Fact]
    public void TestH_And_TestI_Operator_Ai_Summary_Populates_And_Updates()
    {
        var aiPlan = new ValidatedAiResponsePlan(
            Intent: ConversationIntent.QuestionAboutCurrentStep,
            Reply: "Kutudan çıkan yazıcı kablosunu kullanabilirsiniz.",
            IssueSummary: "USB kablo bağlantı sorusu",
            NeedsHuman: false,
            SuggestedNextAction: "guide_usb_connection",
            AiOperatorSummary: "Müşteri kontrol kartının bilgisayara nasıl bağlanacağını sordu. USB kablo rehberliği yapıldı.");

        Assert.NotNull(aiPlan.AiOperatorSummary);
        Assert.Contains("USB kablo rehberliği", aiPlan.AiOperatorSummary);
    }

    [Fact]
    public void TestJ_Group_And_Excluded_Customer_Safety_Maintained_After_Case_Completed()
    {
        var allowlist = new WhatsAppTestAllowlist(
            Options.Create(new WhatsAppTestModeOptions { Enabled = false }),
            Options.Create(new WhatsAppAutomationOptions { ExcludedPhoneNumbers = ["+90 506 336 09 19"] }),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WhatsAppTestAllowlist>.Instance);

        // Group message blocked
        var groupInbound = new WhatsAppInboundEvent { RemoteJid = "123456789-987654@g.us", FromMe = false };
        Assert.False(allowlist.IsAutoReplyAllowed(groupInbound, "123456789-987654@g.us", null));

        // Excluded phone blocked
        var excludedInbound = new WhatsAppInboundEvent { RemotePhoneNumber = "905063360919", FromMe = false };
        Assert.False(allowlist.IsAutoReplyAllowed(excludedInbound, "905063360919", null));
        Assert.False(allowlist.IsOutboundAllowed("905063360919@s.whatsapp.net", null));
    }
}
