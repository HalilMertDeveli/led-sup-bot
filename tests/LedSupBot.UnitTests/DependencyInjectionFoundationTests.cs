using LedSupBot.Application.Abstractions;
using LedSupBot.Infrastructure;
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
}
