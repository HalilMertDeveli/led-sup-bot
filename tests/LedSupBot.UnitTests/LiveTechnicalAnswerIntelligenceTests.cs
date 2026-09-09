using LedSupBot.Application;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Application.Workflow.TechnicalAnswers;
using LedSupBot.Domain.Enums;
using LedSupBot.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LedSupBot.UnitTests;

public sealed class LiveTechnicalAnswerIntelligenceTests(ITestOutputHelper output)
{
    public static TheoryData<string, ConversationIntent> Questions => new()
    {
        { "Colorlight X4e HDMI girişi hangi sinyal formatlarını destekliyor?", ConversationIntent.LedTechnicalQuestion },
        { "Colorlight X4e üzerinde kaç Ethernet çıkışı var?", ConversationIntent.LedTechnicalQuestion },
        { "4 tane E120 var ama ikinci receiverdan sonra görüntü yok, neyi kontrol etmeliyim?", ConversationIntent.LedTroubleshooting },
        { "Receiver kart nedir ve LED ekranda ne işe yarar?", ConversationIntent.LedTechnicalQuestion },
        { "Colorlight X4e için şu an en güncel firmware sürümü nedir?", ConversationIntent.LedTechnicalQuestion }
    };

    [Fact]
    public async Task Real_Gemini_Rag_And_Conditional_Web_Qa()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("LEDSUPBOT_RUN_LIVE_TECH_QA"), "1", StringComparison.Ordinal))
            return;

        var repoConfig = Path.Combine("E:", "led-sup-bot", "src", "LedSupBot.Api", "appsettings.json");
        var localConfig = Path.Combine("E:", "led-sup-bot", "appsettings.Local.json");
        Assert.True(File.Exists(repoConfig));
        Assert.True(File.Exists(localConfig));

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(repoConfig, optional: false)
            .AddJsonFile(localConfig, optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Provider"] = "Gemini",
                ["WebGrounding:Enabled"] = "true",
                ["WebGrounding:MaxAttemptsPerTurn"] = "1",
                ["WebGrounding:TimeoutSeconds"] = "30"
                , ["Gemini:TimeoutSeconds"] = "60"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        foreach (var row in Questions)
        {
            var question = (string)row[0];
            var intent = (ConversationIntent)row[1];
            var context = new ConversationContext
            {
                ConversationId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), BotMode = BotMode.Bot,
                CurrentStage = ConversationStage.Start, StageLabel = "Live QA", KnownFacts = [], MissingFacts = [],
                RecentMessages = []
            };
            var slots = question.Contains("E120", StringComparison.OrdinalIgnoreCase)
                ? new WorkflowSlotState { ReceiverCount = 4, ReceiverModel = ReceiverModel.E120 }
                : new WorkflowSlotState();
            var evidence = await scope.ServiceProvider.GetRequiredService<ITechnicalAnswerContextBuilder>()
                .BuildAsync(new TechnicalAnswerContextRequest(context, slots, question, intent,
                    intent == ConversationIntent.LedTroubleshooting ? question : null,
                    question.Contains("E120", StringComparison.OrdinalIgnoreCase) ? "E120" : null));
            var plan = await scope.ServiceProvider.GetRequiredService<AiResponsePlanner>()
                .TryPlanAsync(context, question, CancellationToken.None);

            Assert.NotNull(plan);
            Assert.True(plan!.HasUsableReply);
            if (question.Contains("X4e", StringComparison.OrdinalIgnoreCase) && !question.Contains("güncel", StringComparison.OrdinalIgnoreCase))
                Assert.Contains(evidence.Spec.DetectedModels, x => x.Equals("X4e", StringComparison.OrdinalIgnoreCase));
            if (question.Contains("güncel", StringComparison.OrdinalIgnoreCase))
                Assert.True(evidence.Web.Attempted);

            output.WriteLine($"QUESTION: {question}");
            output.WriteLine($"SOURCES: {evidence.ToOperatorTrace()}");
            output.WriteLine($"ANSWER: {plan.Reply}");
        }
    }
}
