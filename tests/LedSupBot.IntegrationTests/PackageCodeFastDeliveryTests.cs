using LedSupBot.Application.PanelFiles;
using LedSupBot.Application.WhatsApp;
using LedSupBot.Application.Abstractions;
using LedSupBot.Domain.Enums;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LedSupBot.IntegrationTests;

[Collection("workflow")]
public sealed class PackageCodeFastDeliveryTests(WorkflowFixture fixture)
{
    [Fact]
    public async Task Exact_code_delivers_all_registered_files_before_planner_or_rag()
    {
        var code = $"X{Random.Shared.Next(1000, 9999)}";
        await ImportAsync(code);
        fixture.WhatsApp.SentDocuments.Clear(); fixture.Planner.Reset(); fixture.Knowledge.Reset();

        await HandleAsync($"  P2.5+{code}+2012  ", $"fast-{Guid.NewGuid():N}");

        Assert.Equal(["config.json", "firmware.hex", "readme.txt", "receiver.rcvbp"],
            fixture.WhatsApp.SentDocuments.Select(x => x.FileName));
        Assert.Null(fixture.Planner.LastContext);
        Assert.Empty(fixture.Knowledge.Queries);
        await using var scope = fixture.Services.CreateAsyncScope();
        var messages = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Messages
            .Where(x => x.Direction == MessageDirection.Outbound && x.MessageType == MessageType.Document &&
                x.Content != null && x.Content.Contains(code))
            .ToListAsync();
        Assert.Equal(4, messages.Count);
        Assert.All(messages, x => Assert.Equal(DeliveryStatus.Sent, x.DeliveryStatus));
    }

    [Fact]
    public async Task Similar_or_unknown_code_does_not_deliver_files()
    {
        var code = $"X{Random.Shared.Next(1000, 9999)}";
        await ImportAsync(code);
        fixture.WhatsApp.SentDocuments.Clear();

        await HandleAsync($"P2.5+{code}+201", $"unknown-{Guid.NewGuid():N}");

        Assert.Empty(fixture.WhatsApp.SentDocuments);
    }

    [Fact]
    public async Task Duplicate_inbound_event_does_not_redeliver_package()
    {
        var code = $"X{Random.Shared.Next(1000, 9999)}";
        await ImportAsync(code); fixture.WhatsApp.SentDocuments.Clear();
        var id = $"duplicate-{Guid.NewGuid():N}";
        await HandleAsync($"P2.5+{code}+2012", id);
        await HandleAsync($"P2.5+{code}+2012", id);
        Assert.Equal(4, fixture.WhatsApp.SentDocuments.Count);
    }

    private async Task ImportAsync(string code)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPanelFolderImportService>().ImportAsync($"P2.5+{code}+2012",
        [
            new("receiver.rcvbp", "receiver.rcvbp", "application/octet-stream", new MemoryStream([1])),
            new("firmware.hex", "firmware.hex", "application/octet-stream", new MemoryStream([2])),
            new("config.json", "config.json", "application/json", new MemoryStream([3])),
            new("readme.txt", "readme.txt", "text/plain", new MemoryStream([4]))
        ]);
    }

    private async Task HandleAsync(string text, string id)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IWhatsAppInboundEventHandler>().HandleAsync(new()
        {
            EventType = "IncomingMessage", ExternalMessageId = id, RemotePhoneNumber = "905555551212",
            RemoteJid = "905555551212@s.whatsapp.net", MessageType = "text", TextContent = text, TextPresent = true,
        });
    }
}
