using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LedSupBot.Application.Training;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace LedSupBot.IntegrationTests;

[Collection("whatsapp-webhook")]
public sealed class AiTrainingAdminApiTests
{
    private readonly WhatsAppWebhookFixture _fixture;

    public AiTrainingAdminApiTests(WhatsAppWebhookFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Training_Feedback_Requires_Admin_Authentication()
    {
        var client = _fixture.CreateAdminClient();

        var response = await client.PutAsJsonAsync(
            $"/api/admin/training-feedback/{Guid.NewGuid():D}",
            new { feedbackStatus = "Approved", reasonTags = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Missing_Feedback_Status_Is_Rejected_Instead_Of_Defaulting_To_Approved()
    {
        var seeded = await SeedAiReplyAsync(DeliveryStatus.Delivered);
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/admin/training-feedback/{seeded.AiMessageId:D}",
            new { reasonTags = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = _fixture.CreateDb();
        Assert.False(await db.AiTrainingExamples
            .AnyAsync(x => x.AiResponseMessageId == seeded.AiMessageId));
    }

    [Fact]
    public async Task Only_Delivered_Ai_Bot_Text_With_Snapshot_Is_Teacher_Eligible()
    {
        var seeded = await SeedAiReplyAsync(DeliveryStatus.Delivered);
        Guid technicianMessageId;
        Guid botWithoutSnapshotId;
        Guid systemMessageId;

        await using (var db = _fixture.CreateDb())
        {
            var technicianMessage = new Message
            {
                ConversationId = seeded.ConversationId,
                Direction = MessageDirection.Outbound,
                MessageType = MessageType.Text,
                Content = "manual technician response",
                SentBy = MessageSender.Technician,
                DeliveryStatus = DeliveryStatus.Delivered,
            };
            var botWithoutSnapshot = new Message
            {
                ConversationId = seeded.ConversationId,
                Direction = MessageDirection.Outbound,
                MessageType = MessageType.Text,
                Content = "deterministic non-AI response",
                SentBy = MessageSender.Bot,
                DeliveryStatus = DeliveryStatus.Delivered,
            };
            var systemMessage = new Message
            {
                ConversationId = seeded.ConversationId,
                Direction = MessageDirection.Outbound,
                MessageType = MessageType.System,
                Content = "system event",
                SentBy = MessageSender.System,
                DeliveryStatus = DeliveryStatus.Delivered,
            };
            db.Messages.AddRange(technicianMessage, botWithoutSnapshot, systemMessage);
            await db.SaveChangesAsync();
            technicianMessageId = technicianMessage.Id;
            botWithoutSnapshotId = botWithoutSnapshot.Id;
            systemMessageId = systemMessage.Id;
        }

        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"/api/admin/conversations/{seeded.ConversationId:D}/messages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var messages = document.RootElement.GetProperty("messages")
            .EnumerateArray()
            .ToDictionary(x => x.GetProperty("id").GetGuid());

        Assert.True(messages[seeded.AiMessageId].GetProperty("teacherFeedbackEligible").GetBoolean());
        Assert.False(messages[seeded.CustomerMessageId].GetProperty("teacherFeedbackEligible").GetBoolean());
        Assert.False(messages[technicianMessageId].GetProperty("teacherFeedbackEligible").GetBoolean());
        Assert.False(messages[botWithoutSnapshotId].GetProperty("teacherFeedbackEligible").GetBoolean());
        Assert.False(messages[systemMessageId].GetProperty("teacherFeedbackEligible").GetBoolean());

        var labelRequest = new { feedbackStatus = "Approved", reasonTags = Array.Empty<string>() };
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(
            $"/api/admin/training-feedback/{technicianMessageId:D}", labelRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(
            $"/api/admin/training-feedback/{botWithoutSnapshotId:D}", labelRequest)).StatusCode);
    }

    [Fact]
    public async Task Approved_Feedback_Is_Idempotent_And_Has_No_Production_Side_Effects()
    {
        var seeded = await SeedAiReplyAsync(DeliveryStatus.Delivered);
        var whatsAppCallsBefore = _fixture.WhatsApp.SentTexts.Count;
        await using var beforeDb = _fixture.CreateDb();
        var knowledgeRowsBefore = await beforeDb.KnowledgeDocuments.CountAsync();

        var client = await CreateAuthenticatedClientAsync();
        var first = await client.PutAsJsonAsync(
            $"/api/admin/training-feedback/{seeded.AiMessageId:D}",
            new { feedbackStatus = "Approved", reasonTags = Array.Empty<string>() });
        var repeated = await client.PutAsJsonAsync(
            $"/api/admin/training-feedback/{seeded.AiMessageId:D}",
            new { feedbackStatus = "Approved", reasonTags = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);

        await using var verifyDb = _fixture.CreateDb();
        var examples = await verifyDb.AiTrainingExamples
            .AsNoTracking()
            .Where(x => x.AiResponseMessageId == seeded.AiMessageId)
            .ToListAsync();
        var conversation = await verifyDb.Conversations.AsNoTracking().SingleAsync(x => x.Id == seeded.ConversationId);
        var supportCase = await verifyDb.TechnicalSupportCases.AsNoTracking().SingleAsync(x => x.Id == seeded.CaseId);

        var example = Assert.Single(examples);
        Assert.Equal(AiTrainingFeedbackStatus.Approved, example.FeedbackStatus);
        Assert.Equal(AiTrainingEligibility.EligibleForSft, example.TrainingEligibility);
        Assert.True(example.EligibleForSft);
        Assert.False(example.EligibleForPreference);
        Assert.Equal(BotMode.Bot, conversation.BotMode);
        Assert.Equal(ConversationStage.AskReceiverCount, conversation.CurrentStage);
        AssertJsonEquivalent(seeded.DiagnosticStateJson, supportCase.DiagnosticStateJson!);
        Assert.Equal(2, await verifyDb.Messages.CountAsync(x => x.ConversationId == seeded.ConversationId));
        Assert.Equal(knowledgeRowsBefore, await verifyDb.KnowledgeDocuments.CountAsync());
        Assert.Equal(whatsAppCallsBefore, _fixture.WhatsApp.SentTexts.Count);
    }

    [Fact]
    public async Task Approved_Can_Be_Revised_To_Edited_Without_Rebuilding_Snapshot()
    {
        var seeded = await SeedAiReplyAsync(DeliveryStatus.Read);
        var client = await CreateAuthenticatedClientAsync();

        var approved = await client.PutAsJsonAsync(
            $"/api/admin/training-feedback/{seeded.AiMessageId:D}",
            new { feedbackStatus = "Approved", reasonTags = Array.Empty<string>() });
        var edited = await client.PutAsJsonAsync(
            $"/api/admin/training-feedback/{seeded.AiMessageId:D}",
            new
            {
                feedbackStatus = "Edited",
                preferredTechnicianResponse = "Önce receiver online durumunu kontrol edelim.",
                reasonTags = new[] { "WrongDiagnosticStep", "TooRobotic" },
            });

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        await using var db = _fixture.CreateDb();
        var example = await db.AiTrainingExamples
            .AsNoTracking()
            .SingleAsync(x => x.AiResponseMessageId == seeded.AiMessageId);
        var immutableSnapshot = await db.AiReplyContextSnapshots
            .AsNoTracking()
            .SingleAsync(x => x.AiResponseMessageId == seeded.AiMessageId);

        Assert.Equal(AiTrainingFeedbackStatus.Edited, example.FeedbackStatus);
        Assert.Equal(immutableSnapshot.OriginalAiResponse, example.OriginalAiResponse);
        Assert.Equal("Önce receiver online durumunu kontrol edelim.", example.PreferredTechnicianResponse);
        Assert.Contains("WrongDiagnosticStep", example.FeedbackReasonTags);
        Assert.Equal("[\"ReceiverModel=E120\"]", example.KnownFactsSnapshot);
        Assert.True(example.EligibleForSft);
        Assert.True(example.EligibleForPreference);
        Assert.Equal(1, await db.AiTrainingExamples.CountAsync(x => x.AiResponseMessageId == seeded.AiMessageId));
    }

    [Fact]
    public async Task Rejected_Without_Correction_Is_Not_An_Sft_Or_Preference_Target()
    {
        var seeded = await SeedAiReplyAsync(DeliveryStatus.Delivered);
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/admin/training-feedback/{seeded.AiMessageId:D}",
            new
            {
                feedbackStatus = "Rejected",
                reasonTags = new[] { "WrongContext" },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = _fixture.CreateDb();
        var example = await db.AiTrainingExamples
            .AsNoTracking()
            .SingleAsync(x => x.AiResponseMessageId == seeded.AiMessageId);
        Assert.Equal(AiTrainingFeedbackStatus.Rejected, example.FeedbackStatus);
        Assert.Equal(AiTrainingEligibility.Excluded, example.TrainingEligibility);
        Assert.Null(example.PreferredTechnicianResponse);
        Assert.False(example.EligibleForSft);
        Assert.False(example.EligibleForPreference);
    }

    [Fact]
    public async Task Rejected_With_Correction_Preserves_Preference_Pair_And_Is_Eligible()
    {
        var seeded = await SeedAiReplyAsync(DeliveryStatus.Delivered);
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/admin/training-feedback/{seeded.AiMessageId:D}",
            new
            {
                feedbackStatus = "Rejected",
                preferredTechnicianResponse = "We cannot conclude that yet; first run the receiver test.",
                reasonTags = new[] { "WrongTechnicalInformation", "Hallucination" },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = _fixture.CreateDb();
        var example = await db.AiTrainingExamples
            .AsNoTracking()
            .SingleAsync(x => x.AiResponseMessageId == seeded.AiMessageId);
        var immutableSnapshot = await db.AiReplyContextSnapshots
            .AsNoTracking()
            .SingleAsync(x => x.AiResponseMessageId == seeded.AiMessageId);
        Assert.Equal(immutableSnapshot.OriginalAiResponse, example.OriginalAiResponse);
        Assert.Equal("We cannot conclude that yet; first run the receiver test.", example.PreferredTechnicianResponse);
        Assert.Equal(AiTrainingEligibility.EligibleForPreference, example.TrainingEligibility);
        Assert.True(example.EligibleForSft);
        Assert.True(example.EligibleForPreference);
    }

    [Fact]
    public async Task Feedback_Copies_Immutable_Generation_Time_Context_After_Live_State_Changes()
    {
        const string generationDiagnostic = "activeIssue=black-screen; pendingCheck=power-input";
        var seeded = await SeedAiReplyAsync(
            DeliveryStatus.Read,
            currentIntent: "LedTroubleshooting",
            snapshot: new SnapshotSeed(
                KnownFactsJson: "[\"ReceiverModel=E120\",\"ReceiverCount=4\"]",
                DiagnosticContext: generationDiagnostic,
                WorkflowStage: "AskReceiverCount"));

        await using (var db = _fixture.CreateDb())
        {
            var conversation = await db.Conversations.SingleAsync(x => x.Id == seeded.ConversationId);
            var supportCase = await db.TechnicalSupportCases.SingleAsync(x => x.Id == seeded.CaseId);
            conversation.CurrentStage = ConversationStage.AskScreenSize;
            supportCase.DiagnosticStateJson = "{\"activeIssue\":\"later-correction\",\"receiverModel\":\"E80\"}";
            await db.SaveChangesAsync();
        }

        var client = await CreateAuthenticatedClientAsync();
        var response = await client.PutAsJsonAsync(
            $"/api/admin/training-feedback/{seeded.AiMessageId:D}",
            new { feedbackStatus = "Approved", reasonTags = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verifyDb = _fixture.CreateDb();
        var example = await verifyDb.AiTrainingExamples
            .AsNoTracking()
            .SingleAsync(x => x.AiResponseMessageId == seeded.AiMessageId);
        var originalSnapshot = await verifyDb.AiReplyContextSnapshots
            .AsNoTracking()
            .SingleAsync(x => x.AiResponseMessageId == seeded.AiMessageId);
        var liveConversation = await verifyDb.Conversations.AsNoTracking().SingleAsync(x => x.Id == seeded.ConversationId);
        var liveCase = await verifyDb.TechnicalSupportCases.AsNoTracking().SingleAsync(x => x.Id == seeded.CaseId);

        Assert.Equal(
            new[] { "ReceiverModel=E120", "ReceiverCount=4" },
            JsonSerializer.Deserialize<string[]>(example.KnownFactsSnapshot));
        Assert.Equal(generationDiagnostic, example.DiagnosticContextSnapshot);
        Assert.Equal("AskReceiverCount", example.WorkflowStageSnapshot);
        Assert.Equal(AiReplyContextSnapshot.CurrentSchemaVersion, example.TrainingSnapshotSchemaVersion);
        Assert.Equal(generationDiagnostic, originalSnapshot.DiagnosticContextSnapshot);
        Assert.Equal(ConversationStage.AskScreenSize, liveConversation.CurrentStage);
        Assert.Contains("E80", liveCase.DiagnosticStateJson);
    }

    [Fact]
    public async Task Vision_And_Rag_Provenance_Is_Persisted_Without_Fabricating_Chunk_Content()
    {
        const string sourceReferences =
            "[{\"document\":\"X20-manual.pdf\",\"documentVersion\":\"2.1\",\"pageStart\":12,\"pageEnd\":13,\"section\":\"Output capacity\",\"groundingLevel\":\"GroundedExact\"}]";
        var seeded = await SeedAiReplyAsync(
            DeliveryStatus.Delivered,
            currentIntent: "LedTechnicalQuestion",
            snapshot: new SnapshotSeed(
                SourceDecision: AiTrainingSourceDecision.VisionAndRag,
                RagAttempted: true,
                RagUsed: true,
                GroundingLevel: "GroundedExact",
                GroundingConfidence: .91,
                KnowledgeSourceReferences: sourceReferences,
                VisionUsed: true,
                OcrUsed: true,
                ImageContextSummary: "{\"category\":\"ControllerLabel\",\"validatedModels\":[\"X20\"]}"));
        var client = await CreateAuthenticatedClientAsync();

        var save = await client.PutAsJsonAsync(
            $"/api/admin/training-feedback/{seeded.AiMessageId:D}",
            new { feedbackStatus = "Approved", reasonTags = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        using var savedDocument = JsonDocument.Parse(await save.Content.ReadAsStringAsync());
        var exampleId = savedDocument.RootElement.GetProperty("id").GetGuid();

        var detail = await client.GetAsync($"/api/admin/training-examples/{exampleId:D}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var detailDocument = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        var root = detailDocument.RootElement;
        Assert.Equal("VisionAndRag", root.GetProperty("sourceDecision").GetString());
        Assert.True(root.GetProperty("ragAttempted").GetBoolean());
        Assert.True(root.GetProperty("ragUsed").GetBoolean());
        Assert.True(root.GetProperty("visionUsed").GetBoolean());
        Assert.True(root.GetProperty("ocrUsed").GetBoolean());
        Assert.Equal(.91, root.GetProperty("groundingConfidence").GetDouble(), precision: 2);
        var source = Assert.Single(root.GetProperty("knowledgeSourceReferences").EnumerateArray());
        Assert.Equal("X20-manual.pdf", source.GetProperty("document").GetString());
        Assert.Equal(12, source.GetProperty("pageStart").GetInt32());
        Assert.DoesNotContain("chunk", await detail.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Exact_Technical_Claim_Requires_Review_Even_When_Approved()
    {
        var seeded = await SeedAiReplyAsync(
            DeliveryStatus.Delivered,
            currentIntent: "LedTechnicalQuestion",
            snapshot: new SnapshotSeed(TechnicalClaimRequiresVerification: true));
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/admin/training-feedback/{seeded.AiMessageId:D}",
            new { feedbackStatus = "Approved", reasonTags = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = _fixture.CreateDb();
        var example = await db.AiTrainingExamples
            .AsNoTracking()
            .SingleAsync(x => x.AiResponseMessageId == seeded.AiMessageId);
        Assert.True(example.TechnicalClaimRequiresVerification);
        Assert.Equal(AiTrainingEligibility.NeedsReview, example.TrainingEligibility);
        Assert.False(example.EligibleForSft);
        Assert.False(example.EligibleForPreference);
    }

    [Fact]
    public async Task Pending_Or_Failed_Ai_Message_Cannot_Be_Labelled()
    {
        var pending = await SeedAiReplyAsync(DeliveryStatus.Pending);
        var failed = await SeedAiReplyAsync(DeliveryStatus.Failed);
        var client = await CreateAuthenticatedClientAsync();

        var pendingResponse = await client.PutAsJsonAsync(
            $"/api/admin/training-feedback/{pending.AiMessageId:D}",
            new { feedbackStatus = "Approved", reasonTags = Array.Empty<string>() });
        var failedResponse = await client.PutAsJsonAsync(
            $"/api/admin/training-feedback/{failed.AiMessageId:D}",
            new { feedbackStatus = "Approved", reasonTags = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.Conflict, pendingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, failedResponse.StatusCode);

        await using var db = _fixture.CreateDb();
        Assert.False(await db.AiTrainingExamples.AnyAsync(x =>
            x.AiResponseMessageId == pending.AiMessageId || x.AiResponseMessageId == failed.AiMessageId));
    }

    [Fact]
    public async Task Messages_And_Training_List_Expose_Desktop_Contract()
    {
        var intent = $"Intent{Guid.NewGuid():N}";
        var seeded = await SeedAiReplyAsync(DeliveryStatus.Sent, intent);
        var client = await CreateAuthenticatedClientAsync();

        var save = await client.PutAsJsonAsync(
            $"/api/admin/training-feedback/{seeded.AiMessageId:D}",
            new
            {
                feedbackStatus = "Rejected",
                preferredTechnicianResponse = "Daha güvenli teknisyen yanıtı",
                reasonTags = new[] { "WrongTechnicalInformation", "Hallucination" },
            });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var messages = await client.GetAsync($"/api/admin/conversations/{seeded.ConversationId:D}/messages");
        Assert.Equal(HttpStatusCode.OK, messages.StatusCode);
        using (var document = JsonDocument.Parse(await messages.Content.ReadAsStringAsync()))
        {
            var ai = document.RootElement.GetProperty("messages")
                .EnumerateArray()
                .Single(x => x.GetProperty("id").GetGuid() == seeded.AiMessageId);
            Assert.True(ai.GetProperty("teacherFeedbackEligible").GetBoolean());
            var feedback = ai.GetProperty("trainingFeedback");
            Assert.Equal("Rejected", feedback.GetProperty("feedbackStatus").GetString());
            Assert.Equal("WrongTechnicalInformation", feedback.GetProperty("reasonTags")[0].GetString());
            Assert.True(feedback.GetProperty("eligibleForPreference").GetBoolean());
        }

        var list = await client.GetAsync(
            $"/api/admin/training-examples?status=Rejected&intent={Uri.EscapeDataString(intent)}&sourceDecision=BackendState");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listDocument = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var item = listDocument.RootElement.GetProperty("items")
            .EnumerateArray()
            .Single(x => x.GetProperty("aiResponseMessageId").GetGuid() == seeded.AiMessageId);
        Assert.Equal("Hallucination", item.GetProperty("reasonTags")[1].GetString());
        Assert.Equal("old diagnostic context", item.GetProperty("diagnosticContextSummary").GetString());
        Assert.Equal("gemini-3.6-flash", item.GetProperty("geminiModel").GetString());
    }

    [Fact]
    public async Task Unreviewed_Ai_Snapshot_Does_Not_Automatically_Become_Training_Data()
    {
        var seeded = await SeedAiReplyAsync(DeliveryStatus.Delivered);

        await using var db = _fixture.CreateDb();
        Assert.True(await db.AiReplyContextSnapshots.AnyAsync(x => x.AiResponseMessageId == seeded.AiMessageId));
        Assert.False(await db.AiTrainingExamples.AnyAsync(x => x.AiResponseMessageId == seeded.AiMessageId));
    }

    [Fact]
    public async Task GeneralKnowledge_Metadata_Does_Not_Fabricate_Rag_Usage()
    {
        var seeded = await SeedAiReplyAsync(
            DeliveryStatus.Delivered,
            currentIntent: "GeneralKnowledge",
            snapshot: new SnapshotSeed(
                SourceDecision: AiTrainingSourceDecision.GeneralKnowledge,
                RagAttempted: false,
                RagUsed: false,
                GroundingLevel: "GeneralKnowledge",
                KnowledgeSourceReferences: "[]"));
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/admin/training-feedback/{seeded.AiMessageId:D}",
            new { feedbackStatus = "Approved", reasonTags = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("GeneralKnowledge", root.GetProperty("sourceDecision").GetString());
        Assert.False(root.GetProperty("ragAttempted").GetBoolean());
        Assert.False(root.GetProperty("ragUsed").GetBoolean());
        Assert.Empty(root.GetProperty("knowledgeSourceReferences").EnumerateArray());
    }

    [Fact]
    public async Task Management_Counts_And_All_Basic_Filters_Are_Applied()
    {
        var client = await CreateAuthenticatedClientAsync();
        var beforeResponse = await client.GetAsync("/api/admin/training-examples");
        Assert.Equal(HttpStatusCode.OK, beforeResponse.StatusCode);
        using var beforeDocument = JsonDocument.Parse(await beforeResponse.Content.ReadAsStringAsync());
        var beforeCounts = beforeDocument.RootElement.GetProperty("counts");
        var totalBefore = beforeCounts.GetProperty("total").GetInt32();
        var approvedBefore = beforeCounts.GetProperty("approved").GetInt32();
        var editedBefore = beforeCounts.GetProperty("edited").GetInt32();
        var rejectedBefore = beforeCounts.GetProperty("rejected").GetInt32();
        var sftBefore = beforeCounts.GetProperty("eligibleForSft").GetInt32();
        var preferenceBefore = beforeCounts.GetProperty("eligibleForPreference").GetInt32();
        var tuningReadyBefore = beforeCounts.GetProperty("tuningReady").GetInt32();

        var intent = $"FilterIntent{Guid.NewGuid():N}";
        var from = DateTimeOffset.UtcNow.AddMinutes(-1);
        var approvedSeed = await SeedAiReplyAsync(
            DeliveryStatus.Delivered,
            intent,
            new SnapshotSeed(SourceDecision: AiTrainingSourceDecision.Rag, RagAttempted: true, RagUsed: true));
        var editedSeed = await SeedAiReplyAsync(
            DeliveryStatus.Delivered,
            intent,
            new SnapshotSeed(SourceDecision: AiTrainingSourceDecision.Vision, VisionUsed: true));
        var rejectedSeed = await SeedAiReplyAsync(
            DeliveryStatus.Delivered,
            intent,
            new SnapshotSeed(SourceDecision: AiTrainingSourceDecision.BackendState));

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            $"/api/admin/training-feedback/{approvedSeed.AiMessageId:D}",
            new { feedbackStatus = "Approved", reasonTags = Array.Empty<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            $"/api/admin/training-feedback/{editedSeed.AiMessageId:D}",
            new
            {
                feedbackStatus = "Edited",
                preferredTechnicianResponse = "Preferred filtered response",
                reasonTags = new[] { "Tone" },
            })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            $"/api/admin/training-feedback/{rejectedSeed.AiMessageId:D}",
            new { feedbackStatus = "Rejected", reasonTags = new[] { "WrongContext" } })).StatusCode);
        var to = DateTimeOffset.UtcNow.AddMinutes(1);

        var afterResponse = await client.GetAsync("/api/admin/training-examples");
        Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode);
        using (var afterDocument = JsonDocument.Parse(await afterResponse.Content.ReadAsStringAsync()))
        {
            var counts = afterDocument.RootElement.GetProperty("counts");
            Assert.Equal(totalBefore + 3, counts.GetProperty("total").GetInt32());
            Assert.Equal(approvedBefore + 1, counts.GetProperty("approved").GetInt32());
            Assert.Equal(editedBefore + 1, counts.GetProperty("edited").GetInt32());
            Assert.Equal(rejectedBefore + 1, counts.GetProperty("rejected").GetInt32());
            Assert.Equal(sftBefore + 2, counts.GetProperty("eligibleForSft").GetInt32());
            Assert.Equal(preferenceBefore + 1, counts.GetProperty("eligibleForPreference").GetInt32());
            Assert.Equal(tuningReadyBefore + 2, counts.GetProperty("tuningReady").GetInt32());
            Assert.Equal(200, counts.GetProperty("firstReviewThreshold").GetInt32());
            Assert.Equal(300, counts.GetProperty("firstTuningMin").GetInt32());
            Assert.Equal(500, counts.GetProperty("firstTuningTarget").GetInt32());
            Assert.Contains(
                counts.GetProperty("collectionStatus").GetString(),
                new[]
                {
                    TrainingCollectionProgressPolicy.Collecting,
                    TrainingCollectionProgressPolicy.ReadyForDatasetReview,
                    TrainingCollectionProgressPolicy.TuningCandidateReady,
                });
        }

        var filterUri = "/api/admin/training-examples" +
            $"?status=Approved&intent={Uri.EscapeDataString(intent)}" +
            $"&sourceDecision=Rag&dateFrom={Uri.EscapeDataString(from.ToString("O"))}" +
            $"&dateTo={Uri.EscapeDataString(to.ToString("O"))}";
        var filteredResponse = await client.GetAsync(filterUri);
        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);
        using (var filteredDocument = JsonDocument.Parse(await filteredResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, filteredDocument.RootElement.GetProperty("total").GetInt32());
            var item = Assert.Single(filteredDocument.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(approvedSeed.AiMessageId, item.GetProperty("aiResponseMessageId").GetGuid());
        }

        var futureOnly = await client.GetAsync(
            $"/api/admin/training-examples?intent={Uri.EscapeDataString(intent)}" +
            $"&dateFrom={Uri.EscapeDataString(to.AddDays(1).ToString("O"))}");
        Assert.Equal(HttpStatusCode.OK, futureOnly.StatusCode);
        using var futureDocument = JsonDocument.Parse(await futureOnly.Content.ReadAsStringAsync());
        Assert.Equal(0, futureDocument.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Feedback_Record_Survives_A_Fresh_Application_Host_And_DbContext()
    {
        var seeded = await SeedAiReplyAsync(DeliveryStatus.Read);
        var originalClient = await CreateAuthenticatedClientAsync();
        var save = await originalClient.PutAsJsonAsync(
            $"/api/admin/training-feedback/{seeded.AiMessageId:D}",
            new { feedbackStatus = "Approved", reasonTags = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        using var saveDocument = JsonDocument.Parse(await save.Content.ReadAsStringAsync());
        var exampleId = saveDocument.RootElement.GetProperty("id").GetGuid();
        originalClient.Dispose();

        await using var restartedFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:ConnectionString", _fixture.ConnectionString);
            builder.UseSetting("WhatsAppConnector:Enabled", "false");
            builder.UseSetting("Admin:DashboardPassword", _fixture.AdminPassword);
            builder.UseSetting("Admin:SessionSecret", _fixture.AdminSessionSecret);
            builder.UseSetting("Workflow:AutoReplyEnabled", "false");
            builder.UseSetting("WhatsAppTestMode:Enabled", "false");
        });
        var restartedClient = restartedFactory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        await AuthenticateAsync(restartedClient);

        var detail = await restartedClient.GetAsync($"/api/admin/training-examples/{exampleId:D}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var detailDocument = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal(seeded.AiMessageId, detailDocument.RootElement.GetProperty("aiResponseMessageId").GetGuid());
        Assert.Equal("Approved", detailDocument.RootElement.GetProperty("feedbackStatus").GetString());

        await using var freshDb = _fixture.CreateDb();
        Assert.True(await freshDb.AiTrainingExamples
            .AsNoTracking()
            .AnyAsync(x => x.Id == exampleId && x.AiResponseMessageId == seeded.AiMessageId));
    }

    [Fact]
    public async Task Customer_WhatsApp_Text_Cannot_Create_Or_Update_Training_Feedback()
    {
        await using var beforeDb = _fixture.CreateDb();
        var trainingCountBefore = await beforeDb.AiTrainingExamples.CountAsync();

        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Secret", _fixture.SharedSecret);
        var externalId = $"wamid.customer-feedback.{Guid.NewGuid():N}";
        var phone = $"90{Random.Shared.NextInt64(10000000000, 99999999999)}";
        var response = await client.PostAsJsonAsync("/internal/whatsapp/events", new
        {
            eventType = "IncomingMessage",
            externalMessageId = externalId,
            remotePhoneNumber = phone,
            fromMe = false,
            botOriginated = false,
            messageType = "Text",
            textContent = "bu cevap yanlış, düzelt",
            textPresent = true,
            timestamp = DateTimeOffset.UtcNow,
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await using var verifyDb = _fixture.CreateDb();
        Assert.Equal(trainingCountBefore, await verifyDb.AiTrainingExamples.CountAsync());
        var customerMessage = await verifyDb.Messages
            .AsNoTracking()
            .SingleAsync(x => x.ExternalMessageId == externalId);
        Assert.Equal(MessageSender.Customer, customerMessage.SentBy);
        Assert.Equal(MessageDirection.Inbound, customerMessage.Direction);
    }

    [Fact]
    public async Task Concurrent_Repeated_Approval_Still_Creates_One_Logical_Record()
    {
        var seeded = await SeedAiReplyAsync(DeliveryStatus.Delivered);
        var firstClient = await CreateAuthenticatedClientAsync();
        var secondClient = await CreateAuthenticatedClientAsync();
        var request = new { feedbackStatus = "Approved", reasonTags = Array.Empty<string>() };

        var responses = await Task.WhenAll(
            firstClient.PutAsJsonAsync($"/api/admin/training-feedback/{seeded.AiMessageId:D}", request),
            secondClient.PutAsJsonAsync($"/api/admin/training-feedback/{seeded.AiMessageId:D}", request));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        await using var db = _fixture.CreateDb();
        Assert.Equal(1, await db.AiTrainingExamples.CountAsync(x => x.AiResponseMessageId == seeded.AiMessageId));
    }

    [Fact]
    public async Task Preferred_Response_Does_Not_Persist_Unnecessary_Secrets_Or_Remote_Access_Number()
    {
        var seeded = await SeedAiReplyAsync(DeliveryStatus.Delivered);
        var client = await CreateAuthenticatedClientAsync();
        const string sensitivePreferred =
            @"Connect AnyDesk 123 456 789; token=operator-secret; file C:\private\customer.txt";

        var response = await client.PutAsJsonAsync(
            $"/api/admin/training-feedback/{seeded.AiMessageId:D}",
            new
            {
                feedbackStatus = "Edited",
                preferredTechnicianResponse = sensitivePreferred,
                reasonTags = new[] { "Other" },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = _fixture.CreateDb();
        var stored = await db.AiTrainingExamples
            .AsNoTracking()
            .Where(x => x.AiResponseMessageId == seeded.AiMessageId)
            .Select(x => x.PreferredTechnicianResponse)
            .SingleAsync();
        Assert.NotNull(stored);
        Assert.DoesNotContain("123 456 789", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("operator-secret", stored, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\private", stored, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PF8_Structured_Recognition_Feedback_Preserves_Original_And_Registry()
    {
        var seeded = await SeedAiReplyAsync(DeliveryStatus.Delivered);
        const string original = "{\"panelIdentityAttempted\":true,\"pixelPitch\":\"P2.5\",\"panelCode\":\"1065S\",\"secondaryCode\":\"2012\",\"canonicalPanelKey\":\"P2.5-1065S-2012\",\"pixelPitchSource\":\"CustomerNaturalText\",\"panelCodeSource\":\"PanelLabelOcr\",\"secondaryCodeSource\":\"PanelLabelVision\",\"recognitionStatus\":\"Complete\",\"ocrUsed\":true,\"visionUsed\":true,\"packageResolutionResult\":\"ExactMatch\"}";
        int packageCount; int assetCount;
        await using (var db = _fixture.CreateDb())
        {
            var snapshot = await db.AiReplyContextSnapshots.SingleAsync(x => x.AiResponseMessageId == seeded.AiMessageId);
            snapshot.PanelIdentityContextSnapshot = original;
            packageCount = await db.PanelFilePackages.CountAsync();
            assetCount = await db.PanelFilePackageAssets.CountAsync();
            await db.SaveChangesAsync();
        }

        var client = await CreateAuthenticatedClientAsync();
        var response = await client.PutAsJsonAsync($"/api/admin/training-feedback/{seeded.AiMessageId:D}", new
        {
            feedbackStatus = "Edited", preferredPixelPitch = "p2,5", preferredPanelCode = "1065s",
            preferredSecondaryCode = "2013", reasonTags = Array.Empty<string>()
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verify = _fixture.CreateDb();
        var example = await verify.AiTrainingExamples.AsNoTracking().SingleAsync(x => x.AiResponseMessageId == seeded.AiMessageId);
        AssertJsonEquivalent(original, example.PanelRecognitionSnapshot!);
        Assert.Contains("P2.5-1065S-2013", example.PreferredPanelIdentitySnapshot);
        Assert.Equal(AiTrainingEligibility.EligibleForPreference, example.TrainingEligibility);
        Assert.Equal(packageCount, await verify.PanelFilePackages.CountAsync());
        Assert.Equal(assetCount, await verify.PanelFilePackageAssets.CountAsync());

        var rejected = await client.PutAsJsonAsync($"/api/admin/training-feedback/{seeded.AiMessageId:D}", new
        { feedbackStatus = "Rejected", reasonTags = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        example = await verify.AiTrainingExamples.AsNoTracking().SingleAsync(x => x.AiResponseMessageId == seeded.AiMessageId);
        Assert.Equal(AiTrainingEligibility.Excluded, example.TrainingEligibility);
        AssertJsonEquivalent(original, example.PanelRecognitionSnapshot!);
    }

    private async Task<SeededAiReply> SeedAiReplyAsync(
        DeliveryStatus deliveryStatus,
        string currentIntent = "LedTroubleshooting",
        SnapshotSeed? snapshot = null)
    {
        snapshot ??= new SnapshotSeed();
        await using var db = _fixture.CreateDb();
        var customer = new Customer { PhoneNumber = $"90{Random.Shared.NextInt64(10000000000, 99999999999)}" };
        var conversation = new Conversation
        {
            Customer = customer,
            BotMode = BotMode.Bot,
            Status = ConversationStatus.Active,
            CurrentStage = ConversationStage.AskReceiverCount,
        };
        var supportCase = new TechnicalSupportCase
        {
            Conversation = conversation,
            Status = SupportCaseStatus.CollectingInformation,
            DiagnosticStateJson = "{\"issues\":[],\"activeIssueId\":null}",
        };
        var customerMessage = new Message
        {
            Conversation = conversation,
            Direction = MessageDirection.Inbound,
            MessageType = MessageType.Text,
            Content = "Müşteri sorusu",
            SentBy = MessageSender.Customer,
            DeliveryStatus = DeliveryStatus.Delivered,
            ExternalMessageId = $"wamid.training.in.{Guid.NewGuid():N}",
        };
        var aiMessage = new Message
        {
            Conversation = conversation,
            Direction = MessageDirection.Outbound,
            MessageType = MessageType.Text,
            Content = "AI tarafından üretilen yanıt",
            SentBy = MessageSender.Bot,
            DeliveryStatus = deliveryStatus,
            ExternalMessageId = $"wamid.training.out.{Guid.NewGuid():N}",
        };
        db.AddRange(customer, conversation, supportCase, customerMessage, aiMessage);
        db.AiReplyContextSnapshots.Add(new AiReplyContextSnapshot
        {
            ConversationId = conversation.Id,
            TechnicalSupportCaseId = supportCase.Id,
            CustomerMessageId = customerMessage.Id,
            AiResponseMessageId = aiMessage.Id,
            OriginalCustomerMessage = "Müşteri sorusu",
            OriginalAiResponse = "AI tarafından üretilen yanıt",
            CurrentIntent = currentIntent,
            ResponseAction = "AnswerOnly",
            ReplySource = "Gemini+BackendState",
            GeminiModel = "gemini-3.6-flash",
            PromptPersonaVersion = "conversation-planner-v1",
            KnownFactsSnapshot = snapshot.KnownFactsJson ?? "[\"ReceiverModel=E120\"]",
            MissingFactsSnapshot = "[\"ReceiverCount\"]",
            DeferredMissingFactsSnapshot = "[]",
            PendingWorkflowFactSnapshot = "ReceiverCount",
            WorkflowStageSnapshot = snapshot.WorkflowStage ?? "AskReceiverCount",
            DiagnosticContextSnapshot = snapshot.DiagnosticContext ?? "old diagnostic context",
            DetectedModelsSnapshot = "[\"E120\"]",
            ImageContextSummary = snapshot.ImageContextSummary,
            RagAttempted = snapshot.RagAttempted ?? false,
            RagUsed = snapshot.RagUsed ?? false,
            GroundingLevel = snapshot.GroundingLevel ?? "GeneralKnowledge",
            GroundingConfidence = snapshot.GroundingConfidence ?? 0,
            GroundingConflictDetected = false,
            KnowledgeSourceReferences = snapshot.KnowledgeSourceReferences ?? "[]",
            VisionUsed = snapshot.VisionUsed ?? false,
            OcrUsed = snapshot.OcrUsed ?? false,
            BotModeSnapshot = "Bot",
            SourceDecision = snapshot.SourceDecision ?? AiTrainingSourceDecision.BackendState,
            TechnicalClaimRequiresVerification = snapshot.TechnicalClaimRequiresVerification ?? false,
            RequiresAnonymization = true,
        });
        await db.SaveChangesAsync();

        return new(conversation.Id, supportCase.Id, customerMessage.Id, aiMessage.Id, supportCase.DiagnosticStateJson!);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _fixture.CreateAdminClient();
        await AuthenticateAsync(client);
        return client;
    }

    private async Task AuthenticateAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new { password = _fixture.AdminPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    private static void AssertJsonEquivalent(string expected, string actual)
    {
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
            System.Text.Json.Nodes.JsonNode.Parse(expected),
            System.Text.Json.Nodes.JsonNode.Parse(actual)));
    }

    private sealed record SnapshotSeed(
        string? KnownFactsJson = null,
        string? DiagnosticContext = null,
        string? WorkflowStage = null,
        AiTrainingSourceDecision? SourceDecision = null,
        bool? RagAttempted = null,
        bool? RagUsed = null,
        string? GroundingLevel = null,
        double? GroundingConfidence = null,
        string? KnowledgeSourceReferences = null,
        bool? VisionUsed = null,
        bool? OcrUsed = null,
        string? ImageContextSummary = null,
        bool? TechnicalClaimRequiresVerification = null);

    private sealed record SeededAiReply(
        Guid ConversationId,
        Guid CaseId,
        Guid CustomerMessageId,
        Guid AiMessageId,
        string DiagnosticStateJson);
}
