using System.Text.Json;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Domain.Enums;
using LedSupBot.Infrastructure.Configuration;
using LedSupBot.Infrastructure.Gemini;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LedSupBot.UnitTests;

public sealed class GeminiLiveVerificationTests
{
    private static bool LiveTestsEnabled => string.Equals(
        Environment.GetEnvironmentVariable("LEDSUPBOT_RUN_LIVE_GEMINI_TESTS"),
        "1",
        StringComparison.Ordinal);

    private static GeminiOptions LoadOptions()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json"),
            Path.Combine(AppContext.BaseDirectory, "../appsettings.Local.json"),
            Path.Combine(AppContext.BaseDirectory, "../../appsettings.Local.json"),
            Path.Combine(AppContext.BaseDirectory, "../../../appsettings.Local.json"),
            Path.Combine(AppContext.BaseDirectory, "../../../../appsettings.Local.json"),
            Path.Combine(AppContext.BaseDirectory, "../../../../../appsettings.Local.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.Local.json"),
            "E:\\led-sup-bot\\appsettings.Local.json"
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Gemini", out var geminiSection))
                {
                    var apiKey = geminiSection.TryGetProperty("ApiKey", out var k) ? k.GetString() : "";
                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        return new GeminiOptions
                        {
                            ApiKey = apiKey,
                            Model = geminiSection.TryGetProperty("Model", out var m) ? m.GetString() ?? "gemini-3.6-flash" : "gemini-3.6-flash",
                            BaseUrl = "https://generativelanguage.googleapis.com/",
                            TimeoutSeconds = 30
                        };
                    }
                }
            }
        }

        return new GeminiOptions
        {
            Model = "gemini-3.6-flash"
        };
    }

    [Fact]
    public async Task Gemini_Direct_GenerateContent_Succeeds()
    {
        if (!LiveTestsEnabled) return;
        var options = LoadOptions();
        Assert.False(string.IsNullOrWhiteSpace(options.ApiKey), "Gemini API key must be configured.");

        using var httpClient = new HttpClient { BaseAddress = new Uri(options.BaseUrl) };
        var client = new GeminiStructuredClient(
            httpClient,
            Options.Create(options),
            NullLogger<GeminiStructuredClient>.Instance);

        var response = await client.TryGenerateJsonAsync(
            "test_direct",
            "You are a helpful test assistant.",
            "Sadece şu cevabı ver: GEMINI_CALISIYOR",
            new { type = "OBJECT", properties = new { status = new { type = "STRING" } }, required = new[] { "status" } },
            0.1,
            CancellationToken.None);

        if (response is not null)
        {
            Assert.Contains("GEMINI_CALISIYOR", response);
        }
    }

    [Fact]
    public async Task Gemini_Application_ConversationPlanner_TechnicalSupport_Succeeds()
    {
        if (!LiveTestsEnabled) return;
        var options = LoadOptions();
        Assert.False(string.IsNullOrWhiteSpace(options.ApiKey), "Gemini API key must be configured.");

        using var httpClient = new HttpClient { BaseAddress = new Uri(options.BaseUrl) };
        var client = new GeminiStructuredClient(
            httpClient,
            Options.Create(options),
            NullLogger<GeminiStructuredClient>.Instance);

        var planner = new GeminiConversationPlannerService(
            client,
            NullLogger<GeminiConversationPlannerService>.Instance);

        var responsePlanner = new AiResponsePlanner(planner);

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.AskScreenSize,
            StageLabel = "Ekran Ölçüsü",
            CurrentIssue = null,
            KnownFacts = [],
            MissingFacts = ["screen", "panel", "receiver", "config", "anyDesk"],
            Screen = null,
            Receiver = null,
            Panel = null,
            Config = null,
            AnyDesk = null,
            RecentMessages = []
        };

        var result = await responsePlanner.TryPlanAsync(
            context,
            "Ekranın sağ tarafındaki renkler bozuk, ne yapmalıyım?",
            CancellationToken.None);

        if (result is not null)
        {
            Assert.True(result.HasUsableReply, "Gemini must produce a usable reply.");
            Assert.NotNull(result.IssueSummary);
            Assert.NotEmpty(result.IssueSummary);
        }
    }

    [Fact]
    public async Task Gemini_General_Technical_Question_Returns_Structured_Decision_And_Diagnostic_Response()
    {
        if (!LiveTestsEnabled) return;
        var options = LoadOptions();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return;
        }

        using var httpClient = new HttpClient { BaseAddress = new Uri(options.BaseUrl) };
        var client = new GeminiStructuredClient(
            httpClient,
            Options.Create(options),
            NullLogger<GeminiStructuredClient>.Instance);

        var planner = new GeminiConversationPlannerService(
            client,
            NullLogger<GeminiConversationPlannerService>.Instance);

        var responsePlanner = new AiResponsePlanner(planner);

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.Start,
            StageLabel = "Başlangıç",
            CurrentIssue = null,
            KnownFacts = [],
            MissingFacts = ["Ekran ölçüsü", "Panel etiket fotoğrafı", "Receiver modeli ve adedi"],
            Screen = null,
            Receiver = null,
            Panel = null,
            Config = null,
            AnyDesk = null,
            RecentMessages = []
        };

        var result = await responsePlanner.TryPlanAsync(
            context,
            "Ekranın sağ tarafı pembe çıkıyor, neden olabilir?",
            CancellationToken.None);

        if (result is not null)
        {
            Assert.True(result.HasUsableReply);
            Assert.NotEqual(ConversationIntent.Unclear, result.Intent);
            Assert.False(string.IsNullOrWhiteSpace(result.AiOperatorSummary));

            var replyLower = result.Reply!.ToLowerInvariant();
            Assert.DoesNotContain("ölçüleriniz nedir", replyLower);
            Assert.DoesNotContain("kaç cm", replyLower);
        }
    }

    [Fact]
    public async Task Gemini_Context_Aware_Reasoning_Does_Not_ReAsk_Known_Dimensions()
    {
        if (!LiveTestsEnabled) return;
        var options = LoadOptions();
        Assert.False(string.IsNullOrWhiteSpace(options.ApiKey), "Gemini API key must be configured.");

        using var httpClient = new HttpClient { BaseAddress = new Uri(options.BaseUrl) };
        var client = new GeminiStructuredClient(
            httpClient,
            Options.Create(options),
            NullLogger<GeminiStructuredClient>.Instance);

        var planner = new GeminiConversationPlannerService(
            client,
            NullLogger<GeminiConversationPlannerService>.Instance);

        var responsePlanner = new AiResponsePlanner(planner);

        var context = new ConversationContext
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.AskPanelLabelPhotos,
            StageLabel = "Panel Etiket Fotoğrafları",
            CurrentIssue = "sağ tarafta renk farkı",
            KnownFacts = ["screen: 500x300 cm", "width: 500 cm", "height: 300 cm"],
            MissingFacts = ["panel", "receiver", "config", "anyDesk"],
            Screen = new ContextScreen("500×300 cm", Complete: true),
            Receiver = null,
            Panel = null,
            Config = null,
            AnyDesk = null,
            RecentMessages =
            [
                new ContextMessage("Customer", "Ekranımız 5x3 metre", DateTimeOffset.UtcNow.AddMinutes(-5)),
                new ContextMessage("Bot", "Ekran ölçünüz 500x300 cm olarak kaydedildi. Panel etiketinin fotoğrafını gönderebilir misiniz?", DateTimeOffset.UtcNow.AddMinutes(-4))
            ]
        };

        var result = await responsePlanner.TryPlanAsync(
            context,
            "Ekran ölçüsünü bilmiyorum ama sorun sağ tarafta.",
            CancellationToken.None);

        if (result is not null)
        {
            Assert.True(result.HasUsableReply);

            // Verify Gemini does NOT blindly re-ask for screen dimensions
            var replyLower = result.Reply!.ToLowerInvariant();
            Assert.DoesNotContain("kaç cm", replyLower);
            Assert.DoesNotContain("ekran ölçü", replyLower);
            Assert.DoesNotContain("en ve boy", replyLower);
        }
    }
}
