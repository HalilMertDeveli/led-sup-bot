using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Application.Workflow.Web;
using LedSupBot.Infrastructure;
using LedSupBot.Infrastructure.Gemini;
using LedSupBot.Infrastructure.Queue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LedSupBot.UnitTests;

public class DependencyInjectionFoundationTests
{
    [Fact]
    public void Infrastructure_Registers_InMemory_Work_Queue()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "http://localhost:5173"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        var queue = provider.GetRequiredService<IBackgroundWorkQueue>();

        Assert.IsType<InMemoryBackgroundWorkQueue>(queue);
    }

    [Fact]
    public void Gemini_provider_does_not_register_OpenAI_as_an_automatic_fallback()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Provider"] = "Gemini",
                ["Gemini:ApiKey"] = "test-key",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<GeminiConversationPlannerService>(provider.GetRequiredService<IAiConversationPlanner>());
        Assert.IsType<GeminiWebGroundingService>(provider.GetRequiredService<IWebGroundingService>());
    }
}
