using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Application.Workflow.Knowledge;
using LedSupBot.Application.Workflow.TechnicalAnswers;
using LedSupBot.Application.Workflow.Web;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LedSupBot.IntegrationTests;

[Collection("workflow")]
public sealed class TechnicalAnswerIntelligenceTests(WorkflowFixture fixture)
{
    [Fact]
    public async Task Approved_training_is_retrieved_selectively_and_unapproved_data_is_ignored()
    {
        await SeedTrainingExampleAsync("zirkon hangi config kablosu", "Onaylı kablo açıklama biçimi.",
            AiTrainingFeedbackStatus.Approved, eligible: true);
        await SeedTrainingExampleAsync("zirkon hangi config kablosu", "ONAYSIZ-GİZLİ-CEVAP",
            AiTrainingFeedbackStatus.Excluded, eligible: false);
        await SeedTrainingExampleAsync("zirkon internet bağlantısı nasıl yapılır", "ALAKASIZ-ONAYLI-CEVAP",
            AiTrainingFeedbackStatus.Approved, eligible: true);

        var (conversation, customer) = await fixture.SeedConversationAsync();
        fixture.Knowledge.Reset(); fixture.Web.Reset(); fixture.Planner.Reset(); fixture.WhatsApp.SentTexts.Clear();
        fixture.Planner.NextResponse = new AiResponsePlanRaw(
            "customer_question_about_current_step", "Config için kare uçlu USB-B kablosundan bahsediyorum.",
            null, false, null, ShouldAnswerQuestion: true, ShouldContinueWorkflowNow: false);

        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text,
            "Zirkon, hangi config kablosundan bahsediyorsun?");

        var training = Assert.IsType<ApprovedTrainingSearchResult>(fixture.Planner.LastContext!.ApprovedTraining);
        var match = Assert.Single(training.Examples);
        Assert.Contains("Onaylı", match.ExpectedResponse);
        Assert.DoesNotContain("ONAYSIZ", fixture.Planner.LastContext.ApprovedTrainingContext);
        Assert.DoesNotContain("ALAKASIZ", fixture.Planner.LastContext.ApprovedTrainingContext);
        var support = await fixture.LoadCaseAsync(conversation.Id);
        Assert.Contains("TrainingResults=1", support!.AiOperatorSummary);
    }

    [Fact]
    public async Task Same_customer_history_is_bounded_and_other_customer_history_is_never_exposed()
    {
        var (current, customer) = await fixture.SeedConversationAsync();
        await SeedHistoricalCaseAsync(customer.Id, "E120 ikinci receiver sonrası görüntü kesildi", "aynı müşteri çözümü");
        var (_, otherCustomer) = await fixture.SeedConversationAsync();
        await SeedHistoricalCaseAsync(otherCustomer.Id, "E120 OTHER-CUSTOMER-SECRET", "başka müşteri çözümü");

        fixture.Knowledge.Reset(); fixture.Web.Reset(); fixture.Planner.Reset(); fixture.WhatsApp.SentTexts.Clear();
        fixture.Planner.NextResponse = new AiResponsePlanRaw(
            "led_troubleshooting", "Önce ikinci receiverın çıkışındaki data kablosunu kontrol edin.",
            null, false, "İkinci receiver çıkış kablosunu kontrol edin.", ShouldAnswerQuestion: true,
            ShouldContinueWorkflowNow: false);

        await fixture.ProcessAsync(current.Id, customer.PhoneNumber, Id(), MessageType.Text,
            "E120 ikinci receiver sonrası yine görüntü yok");

        var memory = Assert.IsType<SameCustomerMemoryResult>(fixture.Planner.LastContext!.SameCustomerMemory);
        Assert.True(memory.Used);
        Assert.Contains("aynı müşteri", fixture.Planner.LastContext.SameCustomerMemoryContext);
        Assert.DoesNotContain("OTHER-CUSTOMER-SECRET", fixture.Planner.LastContext.SameCustomerMemoryContext);
        Assert.True(memory.Cases.Count <= 3);
        Assert.All(memory.Cases, item => Assert.True(item.Messages.Count <= 4));
    }

    [Fact]
    public async Task Web_runs_before_final_planner_sanitizes_query_and_preserves_official_source()
    {
        var (conversation, customer) = await fixture.SeedConversationAsync();
        fixture.Knowledge.Reset(); fixture.Web.Reset(); fixture.Planner.Reset(); fixture.WhatsApp.SentTexts.Clear();
        fixture.Web.NextResult = new WebGroundingResult(true, true, true,
            "Resmî kaynağa göre güncel X4e firmware sürümü V9.",
            [new WebSourceReference("Colorlight X4e downloads", "https://www.colorlightinside.com/download/x4e",
                DateTimeOffset.UtcNow, "X4e firmware V9", true)]);
        fixture.Planner.NextResponse = new AiResponsePlanRaw(
            "general_technical_question", "Üreticinin güncel indirme sayfasındaki paket V9 sürümüdür.",
            null, false, null, ShouldAnswerQuestion: true, ShouldContinueWorkflowNow: false);
        var privateGuid = Guid.NewGuid().ToString();

        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text,
            $"X4e için en güncel firmware nedir? +90 555 111 22 33 AnyDesk 123456789 {privateGuid} 123456789@lid");

        Assert.Equal(1, fixture.Web.CallCount);
        Assert.DoesNotContain("555", fixture.Web.LastQuery);
        Assert.DoesNotContain("123456789", fixture.Web.LastQuery);
        Assert.DoesNotContain(privateGuid, fixture.Web.LastQuery);
        Assert.NotNull(fixture.Planner.LastContext!.WebGrounding);
        Assert.True(fixture.Planner.LastContext.WebGrounding!.HasOfficialSource);
        Assert.Contains("colorlightinside.com", fixture.Planner.LastContext.WebEvidenceContext);
        var finalReply = Assert.Single(fixture.WhatsApp.SentTexts);
        Assert.Equal("Üreticinin güncel indirme sayfasındaki paket V9 sürümüdür.", finalReply);
        await using var scope = fixture.Services.CreateAsyncScope();
        var snapshot = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .AiReplyContextSnapshots.AsNoTracking().OrderByDescending(x => x.CreatedAt)
            .FirstAsync(x => x.ConversationId == conversation.Id);
        Assert.Contains("colorlightinside.com", snapshot.WebSourceReferences);
        Assert.Contains("relevantClaim", snapshot.WebSourceReferences);
        Assert.Contains("official", snapshot.WebSourceReferences);
    }

    [Fact]
    public async Task Missing_exact_spec_and_failed_web_never_emit_the_planners_invented_value()
    {
        var (conversation, customer) = await fixture.SeedConversationAsync();
        fixture.Knowledge.Reset(); fixture.Web.Reset(); fixture.Planner.Reset(); fixture.WhatsApp.SentTexts.Clear();
        fixture.Web.NextResult = WebGroundingResult.Failed;
        fixture.Planner.NextResponse = new AiResponsePlanRaw(
            "general_technical_question", "X4e kesin olarak 99999999 piksel destekler.",
            null, false, null, ShouldAnswerQuestion: true, ShouldContinueWorkflowNow: false);

        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text,
            "X4e maksimum kaç piksel destekliyor?");

        Assert.Equal(1, fixture.Web.CallCount);
        Assert.Equal(GroundingLevel.InsufficientEvidence, fixture.Planner.LastContext!.KnowledgeGrounding!.Level);
        var reply = Assert.Single(fixture.WhatsApp.SentTexts);
        Assert.DoesNotContain("99999999", reply);
        Assert.Contains("doğrulanmış", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Multi_fact_question_is_persisted_before_evidence_and_final_reasoning()
    {
        var (conversation, customer) = await fixture.SeedConversationAsync();
        fixture.Knowledge.Reset(); fixture.Web.Reset(); fixture.Planner.Reset(); fixture.WhatsApp.SentTexts.Clear();
        fixture.Planner.NextResponse = new AiResponsePlanRaw(
            "led_troubleshooting", "Önce ikinci E120 çıkışındaki data kablosunu kontrol edin.",
            "İkinci receiver sonrasında görüntü yok", false, "İkinci receiver çıkışını kontrol edin.",
            ShouldAnswerQuestion: true, ShouldContinueWorkflowNow: false);

        await fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, Id(), MessageType.Text,
            "4 tane E120 var ama ikinci karttan sonra görüntü yok");

        Assert.Equal(4, fixture.Planner.LastContext!.Receiver!.Count);
        Assert.Equal(ReceiverModel.E120, fixture.Planner.LastContext.Receiver.Model);
        Assert.Contains(fixture.Planner.LastContext.KnownFacts, x => x.Contains("4 adet E120"));
        var support = await fixture.LoadCaseAsync(conversation.Id);
        Assert.Equal(4, support!.ReceiverInformation!.Count);
        Assert.Equal(ReceiverModel.E120, support.ReceiverInformation.ReceiverModel);
    }

    private async Task SeedTrainingExampleAsync(string question, string response,
        AiTrainingFeedbackStatus status, bool eligible)
    {
        var (conversation, _) = await fixture.SeedConversationAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var customerMessage = new Message
        {
            ConversationId = conversation.Id, Direction = MessageDirection.Inbound, MessageType = MessageType.Text,
            Content = question, ExternalMessageId = Id(), DeliveryStatus = DeliveryStatus.Delivered, SentBy = MessageSender.Customer,
        };
        var botMessage = new Message
        {
            ConversationId = conversation.Id, Direction = MessageDirection.Outbound, MessageType = MessageType.Text,
            Content = response, ExternalMessageId = Id(), DeliveryStatus = DeliveryStatus.Delivered, SentBy = MessageSender.Bot,
        };
        db.Messages.AddRange(customerMessage, botMessage);
        db.AiTrainingExamples.Add(new AiTrainingExample
        {
            ConversationId = conversation.Id,
            CustomerMessageId = customerMessage.Id,
            AiResponseMessageId = botMessage.Id,
            FeedbackStatus = status,
            OriginalCustomerMessage = question,
            OriginalAiResponse = response,
            CurrentIntent = ConversationIntent.QuestionAboutCurrentStep.ToString(),
            TrainingEligibility = eligible ? AiTrainingEligibility.EligibleForSft : AiTrainingEligibility.Excluded,
            EligibleForSft = eligible,
            TechnicalClaimRequiresVerification = false,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedHistoricalCaseAsync(Guid customerId, string summary, string resolutionMessage)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var conversation = new Conversation
        {
            CustomerId = customerId, Status = ConversationStatus.Completed, BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.Completed, ClosedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        db.Conversations.Add(conversation);
        db.TechnicalSupportCases.Add(new TechnicalSupportCase
        {
            ConversationId = conversation.Id, Status = SupportCaseStatus.Completed,
            IssueSummary = summary, SummaryMarkdown = resolutionMessage,
            CompletedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        db.Messages.Add(new Message
        {
            ConversationId = conversation.Id, Direction = MessageDirection.Outbound, MessageType = MessageType.Text,
            Content = resolutionMessage, ExternalMessageId = Id(), DeliveryStatus = DeliveryStatus.Delivered,
            SentBy = MessageSender.Technician,
        });
        await db.SaveChangesAsync();
    }

    private static string Id() => $"tai-{Guid.NewGuid():N}";
}
