using LedSupBot.Application.Abstractions;
using LedSupBot.Application.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LedSupBot.UnitTests;

public sealed class WhatsAppInboundGuardTests
{
    [Fact]
    public async Task FromMe_event_returns_before_database_or_workflow_resolution()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var handler = new WhatsAppInboundEventHandler(
            services,
            new TestConnectionStateStore(),
            new WhatsAppSelfIdentityStore(),
            Options.Create(new WhatsAppTestModeOptions()),
            NullLogger<WhatsAppInboundEventHandler>.Instance);

        await handler.HandleAsync(new WhatsAppInboundEvent
        {
            EventType = "IncomingMessage",
            ExternalMessageId = "self-echo",
            RemoteJid = "123456@lid",
            FromMe = true,
            MessageType = "Text",
            TextContent = "must not enter automation"
        });
    }

    private sealed class TestConnectionStateStore : IWhatsAppConnectionStateStore
    {
        public WhatsAppConnectionStatus Current { get; private set; }

        public void Set(WhatsAppConnectionStatus status) => Current = status;
    }
}
