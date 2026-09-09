using System.Text.Json;
using System.Text.Json.Serialization;
using LedSupBot.Application.Admin;
using LedSupBot.Application.Training;
using LedSupBot.Domain.Enums;

namespace LedSupBot.UnitTests;

public sealed class AiTrainingApiContractTests
{
    private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void Desktop_Shaped_Feedback_Request_Binds_Reason_Tags()
    {
        const string json = """
            {
              "feedbackStatus": "Rejected",
              "preferredTechnicianResponse": "Önce ölçüm yapalım.",
              "reasonTags": ["WrongTechnicalInformation", "Hallucination"]
            }
            """;

        var request = JsonSerializer.Deserialize<AiTrainingFeedbackRequest>(json, ApiJson);

        Assert.NotNull(request);
        Assert.Equal(AiTrainingFeedbackStatus.Rejected, request!.FeedbackStatus);
        Assert.Equal(2, request.ReasonTags!.Count);
        Assert.Contains(AiTrainingFeedbackReason.Hallucination, request.ReasonTags);
    }

    [Fact]
    public void Missing_Feedback_Status_Does_Not_Default_To_Approved()
    {
        var request = JsonSerializer.Deserialize<AiTrainingFeedbackRequest>("{}", ApiJson);

        Assert.NotNull(request);
        Assert.Null(request!.FeedbackStatus);
    }

    [Fact]
    public void Message_Response_Uses_Nested_TrainingFeedback_Contract()
    {
        var response = new MessageDto(
            Guid.NewGuid(),
            MessageDirection.Outbound,
            MessageType.Text,
            "Yanıt",
            MessageSender.Bot,
            DeliveryStatus.Delivered,
            DateTimeOffset.UtcNow,
            null,
            null,
            true,
            new AiTrainingMessageFeedbackDto(
                AiTrainingFeedbackStatus.Edited,
                "Tercih edilen yanıt",
                [AiTrainingFeedbackReason.Tone],
                AiTrainingEligibility.EligibleForPreference,
                true,
                true,
                "✎ Düzeltilmiş"));

        var json = JsonSerializer.Serialize(response, ApiJson);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.GetProperty("teacherFeedbackEligible").GetBoolean());
        var feedback = root.GetProperty("trainingFeedback");
        Assert.Equal("Edited", feedback.GetProperty("feedbackStatus").GetString());
        Assert.Equal("Tone", feedback.GetProperty("reasonTags")[0].GetString());
        Assert.Equal("EligibleForPreference", feedback.GetProperty("trainingEligibility").GetString());
    }

    [Fact]
    public void Training_List_Item_Uses_Desktop_Field_Names()
    {
        var item = new AiTrainingListItemDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            AiTrainingFeedbackStatus.Approved,
            [AiTrainingFeedbackReason.TooRobotic],
            "Müşteri mesajı",
            "AI yanıtı",
            null,
            "Acknowledgement",
            AiTrainingSourceDecision.GeneralKnowledge,
            AiTrainingEligibility.EligibleForSft,
            true,
            false,
            false,
            false,
            "GeneralKnowledge",
            "Aktif tanı yok",
            "gemini-3.6-flash",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(item, ApiJson);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("TooRobotic", root.GetProperty("reasonTags")[0].GetString());
        Assert.Equal("Aktif tanı yok", root.GetProperty("diagnosticContextSummary").GetString());
        Assert.Equal("gemini-3.6-flash", root.GetProperty("geminiModel").GetString());
    }
}
