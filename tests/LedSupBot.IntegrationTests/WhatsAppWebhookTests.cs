using System.Net;
using System.Net.Http.Json;
using LedSupBot.Application.Abstractions;
using LedSupBot.Domain.Enums;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace LedSupBot.IntegrationTests;

public sealed class WhatsAppWebhookFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("led_sup_bot_wa")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();
    public string SharedSecret { get; } = "test-connector-secret";
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using (var db = CreateDb())
        {
            await db.Database.MigrateAsync();
        }

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:ConnectionString", ConnectionString);
            builder.UseSetting("WhatsAppConnector:Enabled", "true");
            builder.UseSetting("WhatsAppConnector:SharedSecret", SharedSecret);
            builder.UseSetting("WhatsAppConnector:BaseUrl", "http://127.0.0.1:9");
        });
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}

[CollectionDefinition("whatsapp-webhook")]
public sealed class WhatsAppWebhookCollection : ICollectionFixture<WhatsAppWebhookFixture>;

[Collection("whatsapp-webhook")]
public sealed class WhatsAppWebhookTests
{
    private readonly WhatsAppWebhookFixture _fixture;

    public WhatsAppWebhookTests(WhatsAppWebhookFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Rejects_Unauthorized_Webhook()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/internal/whatsapp/events", new
        {
            eventType = "IncomingMessage",
            externalMessageId = "x"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_Unauthorized_Status_And_SendText()
    {
        var client = _fixture.Factory.CreateClient();

        var status = await client.GetAsync("/internal/whatsapp/status");
        Assert.Equal(HttpStatusCode.Unauthorized, status.StatusCode);

        var send = await client.PostAsJsonAsync("/internal/whatsapp/send-text", new { to = "90555", text = "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, send.StatusCode);
    }

    [Fact]
    public async Task SendText_Returns_503_When_Connector_Offline()
    {
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Secret", _fixture.SharedSecret);

        var response = await client.PostAsJsonAsync("/internal/whatsapp/send-text", new
        {
            to = "905551000099",
            text = "controlled offline probe"
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Persists_Inbound_Customer_Message_Once()
    {
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Secret", _fixture.SharedSecret);

        var externalId = $"wamid.test.{Guid.NewGuid():N}";
        var payload = new
        {
            eventType = "IncomingMessage",
            externalMessageId = externalId,
            remotePhoneNumber = "905551000001",
            fromMe = false,
            botOriginated = false,
            messageType = "Text",
            textContent = "hello from test",
            textPresent = true,
            timestamp = DateTimeOffset.UtcNow
        };

        var first = await client.PostAsJsonAsync("/internal/whatsapp/events", payload);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        var second = await client.PostAsJsonAsync("/internal/whatsapp/events", payload);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);

        await using var db = _fixture.CreateDb();
        var count = await db.Messages.CountAsync(m => m.ExternalMessageId == externalId);
        Assert.Equal(1, count);

        var message = await db.Messages.SingleAsync(m => m.ExternalMessageId == externalId);
        Assert.Equal(MessageSender.Customer, message.SentBy);
        Assert.Equal(MessageDirection.Inbound, message.Direction);
    }

    [Fact]
    public async Task FromMe_Without_BotOriginated_Maps_Technician_And_Sets_Human_Mode()
    {
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Secret", _fixture.SharedSecret);

        var phone = "905551000002";
        var customerMsg = new
        {
            eventType = "IncomingMessage",
            externalMessageId = $"wamid.cust.{Guid.NewGuid():N}",
            remotePhoneNumber = phone,
            fromMe = false,
            botOriginated = false,
            messageType = "Text",
            textContent = "customer",
            textPresent = true,
            timestamp = DateTimeOffset.UtcNow
        };
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/internal/whatsapp/events", customerMsg)).StatusCode);

        var techMsg = new
        {
            eventType = "IncomingMessage",
            externalMessageId = $"wamid.tech.{Guid.NewGuid():N}",
            remotePhoneNumber = phone,
            fromMe = true,
            botOriginated = false,
            messageType = "Text",
            textContent = "halil reply",
            textPresent = true,
            timestamp = DateTimeOffset.UtcNow
        };
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/internal/whatsapp/events", techMsg)).StatusCode);

        await using var db = _fixture.CreateDb();
        var conversation = await db.Conversations
            .Include(c => c.Customer)
            .SingleAsync(c => c.Customer.PhoneNumber == phone);
        Assert.Equal(BotMode.Human, conversation.BotMode);

        var message = await db.Messages.SingleAsync(m => m.ExternalMessageId == techMsg.externalMessageId);
        Assert.Equal(MessageSender.Technician, message.SentBy);
    }

    [Fact]
    public async Task ConnectionStateChanged_Updates_Store()
    {
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Secret", _fixture.SharedSecret);

        var response = await client.PostAsJsonAsync("/internal/whatsapp/events", new
        {
            eventType = "ConnectionStateChanged",
            status = "CONNECTED",
            timestamp = DateTimeOffset.UtcNow
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var scope = _fixture.Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IWhatsAppConnectionStateStore>();
        Assert.Equal(WhatsAppConnectionStatus.Connected, store.Current);
    }

    [Fact]
    public async Task Bot_Echo_With_Same_ExternalId_Does_Not_Flip_To_Human()
    {
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Secret", _fixture.SharedSecret);

        var phone = "905551000003";
        var externalId = $"wamid.bot.{Guid.NewGuid():N}";

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/internal/whatsapp/events", new
        {
            eventType = "IncomingMessage",
            externalMessageId = $"wamid.cust.{Guid.NewGuid():N}",
            remotePhoneNumber = phone,
            fromMe = false,
            botOriginated = false,
            messageType = "Text",
            textContent = "hi",
            textPresent = true,
            timestamp = DateTimeOffset.UtcNow
        })).StatusCode);

        await using (var db = _fixture.CreateDb())
        {
            var conversation = await db.Conversations
                .Include(c => c.Customer)
                .SingleAsync(c => c.Customer.PhoneNumber == phone);

            db.Messages.Add(new Domain.Entities.Message
            {
                ConversationId = conversation.Id,
                Direction = MessageDirection.Outbound,
                MessageType = MessageType.Text,
                Content = "bot text",
                ExternalMessageId = externalId,
                DeliveryStatus = DeliveryStatus.Sent,
                SentBy = MessageSender.Bot
            });
            await db.SaveChangesAsync();
            Assert.Equal(BotMode.Bot, conversation.BotMode);
        }

        // Connector restart lost memory: botOriginated=false, but id already recorded as Bot.
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/internal/whatsapp/events", new
        {
            eventType = "IncomingMessage",
            externalMessageId = externalId,
            remotePhoneNumber = phone,
            fromMe = true,
            botOriginated = false,
            messageType = "Text",
            textContent = "bot text",
            textPresent = true,
            timestamp = DateTimeOffset.UtcNow
        })).StatusCode);

        await using var verify = _fixture.CreateDb();
        var after = await verify.Conversations
            .Include(c => c.Customer)
            .SingleAsync(c => c.Customer.PhoneNumber == phone);
        Assert.Equal(BotMode.Bot, after.BotMode);
        Assert.Equal(1, await verify.Messages.CountAsync(m => m.ExternalMessageId == externalId));
    }

    [Fact]
    public async Task Image_Message_Persists_Without_Breaking_Text_Flow()
    {
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Secret", _fixture.SharedSecret);

        var externalId = $"wamid.img.{Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync("/internal/whatsapp/events", new
        {
            eventType = "IncomingMessage",
            externalMessageId = externalId,
            remotePhoneNumber = "905551000004",
            fromMe = false,
            botOriginated = false,
            messageType = "Image",
            textContent = "label photo",
            textPresent = true,
            timestamp = DateTimeOffset.UtcNow,
            media = new { mimeType = "image/jpeg", downloadable = true, fileLength = 12345 }
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        await using var db = _fixture.CreateDb();
        var message = await db.Messages.SingleAsync(m => m.ExternalMessageId == externalId);
        Assert.Equal(MessageType.Image, message.MessageType);
        Assert.Equal(MessageSender.Customer, message.SentBy);
    }
}
