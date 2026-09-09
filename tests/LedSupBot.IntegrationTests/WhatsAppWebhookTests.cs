using System.Net;
using System.Net.Http.Json;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.PanelFiles;
using LedSupBot.Application.WhatsApp;
using LedSupBot.Domain.Enums;
using LedSupBot.Domain.Entities;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace LedSupBot.IntegrationTests;

public sealed class WhatsAppWebhookFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("led_sup_bot_wa")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();
    public string SharedSecret { get; } = "test-connector-secret";
    public string AdminPassword { get; } = "test-admin-password";
    public string AdminSessionSecret { get; } = "test-admin-session-secret-minimum-length";
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public RecordingWhatsAppClient WhatsApp { get; } = new();
    public string MediaRoot { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        MediaRoot = Path.Combine(Path.GetTempPath(), "led-sup-bot-admin-media", Guid.NewGuid().ToString("N"));

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:ConnectionString", ConnectionString);
            builder.UseSetting("WhatsAppConnector:Enabled", "true");
            builder.UseSetting("WhatsAppConnector:SharedSecret", SharedSecret);
            builder.UseSetting("WhatsAppConnector:BaseUrl", "http://127.0.0.1:9");
            builder.UseSetting("Admin:DashboardPassword", AdminPassword);
            builder.UseSetting("Admin:SessionSecret", AdminSessionSecret);
            builder.UseSetting("Workflow:AutoReplyEnabled", "false");
            builder.UseSetting("MediaStorage:RootPath", MediaRoot);
            builder.UseSetting("PanelFilePackages:MaxFirmwareFileSizeBytes", "4");
            builder.UseSetting("PanelFilePackages:MaxReceiverConfigFileSizeBytes", "4");
            builder.UseSetting("WhatsAppTestMode:Enabled", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IWhatsAppClient>();
                services.AddSingleton(WhatsApp);
                services.AddSingleton<IWhatsAppClient>(sp => sp.GetRequiredService<RecordingWhatsAppClient>());
            });
        });

        // Starting the real API host must bring an existing or empty database to the current schema.
        using var startupClient = Factory.CreateClient();
        await using var db = CreateDb();
        if ((await db.Database.GetPendingMigrationsAsync()).Any())
            throw new InvalidOperationException("API startup left pending database migrations.");
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        if (Directory.Exists(MediaRoot)) Directory.Delete(MediaRoot, true);
        await _postgres.DisposeAsync();
    }

    public ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    public HttpClient CreateAdminClient() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
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
    public async Task Contact_update_merges_trusted_lid_customer_and_preserves_route()
    {
        var suffix = Random.Shared.Next(100000, 999999).ToString();
        var phone = "90555" + suffix;
        var lid = Random.Shared.NextInt64(100000000000000, 999999999999999) + "@lid";
        Guid conversationId;
        await using (var db = _fixture.CreateDb())
        {
            var canonical = new Customer { PhoneNumber = phone };
            var lidCustomer = new Customer { PhoneNumber = lid };
            db.Customers.AddRange(canonical, lidCustomer);
            var conversation = new Conversation { CustomerId = lidCustomer.Id, LastInboundWhatsAppAddress = lid };
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync();
            conversationId = conversation.Id;
        }

        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Secret", _fixture.SharedSecret);
        var response = await client.PostAsJsonAsync("/internal/whatsapp/events", new
        {
            eventType = "ContactUpdated", remoteJid = lid, lidJid = lid,
            phoneJid = phone + "@s.whatsapp.net", savedDisplayName = "Ahmet LED"
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var poll = _fixture.CreateDb();
            var updated = await poll.Conversations.Include(x => x.Customer).SingleAsync(x => x.Id == conversationId);
            if (updated.Customer.DisplayName == "Ahmet LED") break;
            await Task.Delay(50);
        }

        await using var verify = _fixture.CreateDb();
        var actual = await verify.Conversations.Include(x => x.Customer).SingleAsync(x => x.Id == conversationId);
        Assert.Equal("Ahmet LED", actual.Customer.DisplayName);
        Assert.Equal(phone, actual.Customer.PhoneNumber);
        Assert.Equal(lid, actual.Customer.LinkedWhatsAppLid);
        Assert.Equal(lid, actual.LastInboundWhatsAppAddress);
        Assert.Single(await verify.Customers.Where(x => x.PhoneNumber == phone || x.PhoneNumber == lid).ToListAsync());
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
        _fixture.WhatsApp.SimulateConnectorUnavailable = true;
        try
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
        finally
        {
            _fixture.WhatsApp.SimulateConnectorUnavailable = false;
        }
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
    public async Task FromMe_Without_BotOriginated_Does_Not_Enter_Customer_Pipeline()
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
        Assert.Equal(ConversationStage.HumanHandoff, conversation.CurrentStage);
        Assert.Equal(ConversationStatus.InHumanSupport, conversation.Status);
        Assert.True(await db.Messages.AnyAsync(m => m.ExternalMessageId == techMsg.externalMessageId && m.SentBy == MessageSender.Technician));
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
    public async Task Customer_Inbound_AutoReply_Sends_When_Enabled()
    {
        await using var factory = await CreateAutoReplyFactoryAsync();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Secret", _fixture.SharedSecret);

        var externalId = $"wamid.autoreply.{Guid.NewGuid():N}";
        var phone = "905551000099";
        var response = await client.PostAsJsonAsync("/internal/whatsapp/events", new
        {
            eventType = "IncomingMessage",
            externalMessageId = externalId,
            remotePhoneNumber = phone,
            fromMe = false,
            botOriginated = false,
            messageType = "Text",
            textContent = "TEST-AUTO-REPLY-E2E",
            textPresent = true,
            timestamp = DateTimeOffset.UtcNow
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await WaitForSentTextAsync();
        Assert.NotEmpty(_fixture.WhatsApp.SentTexts);
        Assert.Contains(_fixture.WhatsApp.SentTexts, t =>
            t.Contains("cm", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("ekran", StringComparison.OrdinalIgnoreCase));

        await using var db = _fixture.CreateDb();
        var botOutbound = await db.Messages
            .Where(m => m.ExternalMessageId != externalId && m.SentBy == MessageSender.Bot)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(botOutbound);
        Assert.Equal(MessageDirection.Outbound, botOutbound!.Direction);
    }

    [Fact]
    public async Task Duplicate_Customer_Inbound_Retries_Workflow_When_No_Bot_Reply_Yet()
    {
        await using var factory = await CreateAutoReplyFactoryAsync();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Secret", _fixture.SharedSecret);

        var externalId = $"wamid.dup.{Guid.NewGuid():N}";
        var phone = "905551000100";

        await using (var db = _fixture.CreateDb())
        {
            var customer = new Domain.Entities.Customer { PhoneNumber = phone };
            db.Customers.Add(customer);
            var conversation = new Domain.Entities.Conversation
            {
                CustomerId = customer.Id,
                Status = ConversationStatus.Active,
                BotMode = BotMode.Bot,
                CurrentStage = ConversationStage.AskScreenSize,
            };
            db.Conversations.Add(conversation);
            db.Messages.Add(new Domain.Entities.Message
            {
                ConversationId = conversation.Id,
                Direction = MessageDirection.Inbound,
                MessageType = MessageType.Text,
                Content = "TEST-DUP-RETRY",
                ExternalMessageId = externalId,
                DeliveryStatus = DeliveryStatus.Delivered,
                SentBy = MessageSender.Customer,
            });
            await db.SaveChangesAsync();
        }

        _fixture.WhatsApp.SentTexts.Clear();
        var response = await client.PostAsJsonAsync("/internal/whatsapp/events", new
        {
            eventType = "IncomingMessage",
            externalMessageId = externalId,
            remotePhoneNumber = phone,
            fromMe = false,
            botOriginated = false,
            messageType = "Text",
            textContent = "TEST-DUP-RETRY",
            textPresent = true,
            timestamp = DateTimeOffset.UtcNow
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await WaitForSentTextAsync();
        Assert.NotEmpty(_fixture.WhatsApp.SentTexts);
    }

    private async Task WaitForSentTextAsync()
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        while (_fixture.WhatsApp.SentTexts.Count == 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(100);
        }
    }

    private async Task<WebApplicationFactory<Program>> CreateAutoReplyFactoryAsync(
        bool testModeEnabled = false,
        string allowedPhone = "")
    {
        _fixture.WhatsApp.SentTexts.Clear();

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:ConnectionString", _fixture.ConnectionString);
            builder.UseSetting("WhatsAppConnector:Enabled", "true");
            builder.UseSetting("WhatsAppConnector:SharedSecret", _fixture.SharedSecret);
            builder.UseSetting("WhatsAppConnector:BaseUrl", "http://127.0.0.1:9");
            builder.UseSetting("Admin:DashboardPassword", _fixture.AdminPassword);
            builder.UseSetting("Admin:SessionSecret", _fixture.AdminSessionSecret);
            builder.UseSetting("Workflow:AutoReplyEnabled", "true");
            builder.UseSetting("Workflow:InboundCoalescing:Enabled", "false");
            builder.UseSetting("Workflow:NaturalTiming:Enabled", "false");
            builder.UseSetting("WhatsAppTestMode:Enabled", testModeEnabled.ToString());
            if (testModeEnabled && !string.IsNullOrWhiteSpace(allowedPhone))
            {
                builder.UseSetting("WhatsAppTestMode:AllowedPhoneNumber", allowedPhone);
            }

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IWhatsAppClient>();
                services.AddSingleton(_fixture.WhatsApp);
                services.AddSingleton<IWhatsAppClient>(sp => sp.GetRequiredService<RecordingWhatsAppClient>());
                var selfStore = new WhatsAppSelfIdentityStore();
                selfStore.SetSelfJids(["905469124845@s.whatsapp.net", "268040956571702@lid"]);
                services.RemoveAll<IWhatsAppSelfIdentityStore>();
                services.AddSingleton<IWhatsAppSelfIdentityStore>(selfStore);
            });
        });

        await using var db = _fixture.CreateDb();
        await db.Database.MigrateAsync();
        return factory;
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

    // ======================= BOT RUNTIME PIPELINE (BOT-03..07, BOT-12) =======

    [Fact]
    public async Task BOT_03_authorized_test_mode_customer_allowed()
    {
        await using var factory = await CreateAutoReplyFactoryAsync(testModeEnabled: true, allowedPhone: "905555000201");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Secret", _fixture.SharedSecret);

        var response = await client.PostAsJsonAsync("/internal/whatsapp/events", new
        {
            eventType = "IncomingMessage",
            externalMessageId = $"wamid.bot03.{Guid.NewGuid():N}",
            remotePhoneNumber = "905555000201",
            fromMe = false,
            botOriginated = false,
            messageType = "Text",
            textContent = "BOT-03-HELLO",
            textPresent = true,
            timestamp = DateTimeOffset.UtcNow
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await WaitForSentTextAsync();
        Assert.NotEmpty(_fixture.WhatsApp.SentTexts);
    }

    [Fact]
    public async Task BOT_04_same_customer_via_lid_passes_trusted_allowlist()
    {
        await using var factory = await CreateAutoReplyFactoryAsync(testModeEnabled: true, allowedPhone: "905555000202");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Secret", _fixture.SharedSecret);

        var lidAddress = "261040956571702@lid";
        var phone = "905555000202";

        // Pre-create the customer with a trusted LID link (as WhatsAppTestMode store learns it).
        await using (var db = _fixture.CreateDb())
        {
            var customer = new Domain.Entities.Customer
            {
                PhoneNumber = phone,
                LinkedWhatsAppLid = lidAddress,
            };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync("/internal/whatsapp/events", new
        {
            eventType = "IncomingMessage",
            externalMessageId = $"wamid.bot04.{Guid.NewGuid():N}",
            remoteJid = lidAddress,
            remotePhoneNumber = (string?)null,
            fromMe = false,
            botOriginated = false,
            messageType = "Text",
            textContent = "BOT-04-HELLO",
            textPresent = true,
            timestamp = DateTimeOffset.UtcNow
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await WaitForSentTextAsync();
        Assert.NotEmpty(_fixture.WhatsApp.SentTexts);
    }

    [Fact]
    public async Task BOT_05_unapproved_customer_blocked_in_test_mode()
    {
        await using var factory = await CreateAutoReplyFactoryAsync(testModeEnabled: true, allowedPhone: "905555000203");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Secret", _fixture.SharedSecret);

        _fixture.WhatsApp.SentTexts.Clear();
        var response = await client.PostAsJsonAsync("/internal/whatsapp/events", new
        {
            eventType = "IncomingMessage",
            externalMessageId = $"wamid.bot05.{Guid.NewGuid():N}",
            remotePhoneNumber = "905559990888",
            fromMe = false,
            botOriginated = false,
            messageType = "Text",
            textContent = "BOT-05-STRANGER",
            textPresent = true,
            timestamp = DateTimeOffset.UtcNow
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Empty(_fixture.WhatsApp.SentTexts);
    }

    [Fact]
    public async Task Exact_package_request_through_webhook_sends_every_asset_to_trusted_lid_route()
    {
        await using var factory = await CreateAutoReplyFactoryAsync();
        var code = $"{Random.Shared.Next(100000, 999999)}S";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPanelFolderImportService>().ImportAsync(
                $"P2.5+{code}+2012",
                [
                    new("random-a.hex", "random-a.hex", "application/octet-stream", new MemoryStream([1])),
                    new("random-b.rcvbp", "random-b.rcvbp", "application/octet-stream", new MemoryStream([2])),
                    new("random-c.bin", "random-c.bin", "application/octet-stream", new MemoryStream([3]))
                ]);
        }

        _fixture.WhatsApp.SentDocuments.Clear();
        _fixture.WhatsApp.SentDocumentDestinations.Clear();
        var trustedLid = $"{Random.Shared.NextInt64(100000000000000, 999999999999999)}@lid";
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Secret", _fixture.SharedSecret);
        var response = await client.PostAsJsonAsync("/internal/whatsapp/events", new
        {
            eventType = "IncomingMessage",
            externalMessageId = $"wamid.package.{Guid.NewGuid():N}",
            remoteJid = trustedLid,
            remotePhoneNumber = "905555000299",
            fromMe = false,
            botOriginated = false,
            messageType = "Text",
            textContent = $"P2.5+{code}+2012 dosyalarını gönder.",
            textPresent = true,
            timestamp = DateTimeOffset.UtcNow
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        for (var i = 0; i < 100 && _fixture.WhatsApp.SentDocuments.Count < 3; i++)
            await Task.Delay(50);
        Assert.Equal(new[] { "random-a.hex", "random-b.rcvbp", "random-c.bin" },
            _fixture.WhatsApp.SentDocuments.Select(x => x.FileName));
        Assert.Equal(3, _fixture.WhatsApp.SentDocumentDestinations.Count);
        Assert.All(_fixture.WhatsApp.SentDocumentDestinations, destination => Assert.Equal(trustedLid, destination));
    }
}
