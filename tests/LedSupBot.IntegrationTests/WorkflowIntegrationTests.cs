using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Media;
using LedSupBot.Application.Workflow;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using LedSupBot.Infrastructure.Persistence;
using LedSupBot.UnitTests.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace LedSupBot.IntegrationTests;

public sealed class WorkflowFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("led_sup_bot_workflow")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();
    public string MediaRoot { get; private set; } = string.Empty;
    public IServiceProvider Services { get; private set; } = null!;
    public RecordingWhatsAppClient WhatsApp { get; private set; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        MediaRoot = Path.Combine(Path.GetTempPath(), "led-sup-bot-workflow-media", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(MediaRoot);

        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = ConnectionString,
                ["Workflow:AutoReplyEnabled"] = "false",
                ["Workflow:ConfigExampleImagePath"] = "",
                ["WhatsAppConnector:Enabled"] = "false",
                ["MediaStorage:RootPath"] = MediaRoot,
                ["MediaStorage:MaxFileSizeBytes"] = "10485760",
            })
            .Build();

        LedSupBot.Application.DependencyInjection.AddApplication(services);
        LedSupBot.Infrastructure.DependencyInjection.AddInfrastructure(services, configuration);

        services.AddSingleton<RecordingWhatsAppClient>();
        services.AddSingleton<IWhatsAppClient>(sp => sp.GetRequiredService<RecordingWhatsAppClient>());

        Services = services.BuildServiceProvider();
        WhatsApp = Services.GetRequiredService<RecordingWhatsAppClient>();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (Services is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (Services is IDisposable disposable)
        {
            disposable.Dispose();
        }

        await _postgres.DisposeAsync();

        try
        {
            if (Directory.Exists(MediaRoot))
            {
                Directory.Delete(MediaRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort temp cleanup.
        }
    }

    public async Task<(Conversation Conversation, Customer Customer)> SeedConversationAsync(string? phone = null)
    {
        phone ??= $"90555{Random.Shared.Next(1_000_000, 9_999_999)}";
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var customer = new Customer { PhoneNumber = phone };
        db.Customers.Add(customer);

        var conversation = new Conversation
        {
            CustomerId = customer.Id,
            Status = ConversationStatus.Active,
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.Start,
        };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        return (conversation, customer);
    }

    public async Task ProcessAsync(Guid conversationId, string phone, string externalId, MessageType type, string? text)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var workflow = scope.ServiceProvider.GetRequiredService<IConversationWorkflowService>();

        var message = new Message
        {
            ConversationId = conversationId,
            Direction = MessageDirection.Inbound,
            MessageType = type,
            Content = text,
            ExternalMessageId = externalId,
            DeliveryStatus = DeliveryStatus.Delivered,
            SentBy = MessageSender.Customer,
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();

        if (type == MessageType.Image)
        {
            WhatsApp.SetDownload(externalId, "image/png", TestImageBytes.CreatePng());
            var pipeline = scope.ServiceProvider.GetRequiredService<IInboundMediaPipeline>();
            var supportCase = await db.TechnicalSupportCases
                .FirstOrDefaultAsync(c => c.ConversationId == conversationId);
            if (supportCase is null)
            {
                supportCase = new TechnicalSupportCase
                {
                    ConversationId = conversationId,
                    Status = SupportCaseStatus.CollectingInformation,
                };
                db.TechnicalSupportCases.Add(supportCase);
                await db.SaveChangesAsync();
            }

            var trackedMessage = await db.Messages.SingleAsync(m => m.Id == message.Id);
            await pipeline.ProcessInboundImageAsync(
                trackedMessage,
                supportCase,
                new WhatsAppInboundEvent
                {
                    ExternalMessageId = externalId,
                    MessageType = "Image",
                    Media = new WhatsAppMediaMetadata
                    {
                        MimeType = "image/png",
                        Downloadable = true,
                        FileLength = TestImageBytes.CreatePng().LongLength,
                    },
                });
        }

        db.ChangeTracker.Clear();

        await workflow.ProcessCustomerInboundAsync(
            new WorkflowInboundMessage(conversationId, phone, message.Id, externalId, type, text));
    }

    public async Task<TechnicalSupportCase?> LoadCaseAsync(Guid conversationId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.TechnicalSupportCases
            .Include(c => c.ScreenInformation)
            .Include(c => c.ReceiverInformation)
            .Include(c => c.ConfigConnection)
            .Include(c => c.AnyDeskInformation)
            .Include(c => c.PanelLabelPhotos)
            .FirstOrDefaultAsync(c => c.ConversationId == conversationId);
    }

    public async Task<Conversation> LoadConversationAsync(Guid conversationId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Conversations.SingleAsync(c => c.Id == conversationId);
    }
}

[CollectionDefinition("workflow")]
public sealed class WorkflowCollection : ICollectionFixture<WorkflowFixture>;

[Collection("workflow")]
public sealed class WorkflowIntegrationTests
{
    private readonly WorkflowFixture _fixture;

    public WorkflowIntegrationTests(WorkflowFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task New_Conversation_Advances_Stage_And_Prompts_Screen_Size()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync();

        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "Merhaba");

        var updated = await _fixture.LoadConversationAsync(conversation.Id);
        Assert.Equal(ConversationStage.AskScreenSize, updated.CurrentStage);
    }

    [Fact]
    public async Task Screen_Size_500x300_Advances_To_Panel_Photos()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync();
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "Merhaba");
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "500 cm x 300 cm");

        var supportCase = await _fixture.LoadCaseAsync(conversation.Id);
        Assert.NotNull(supportCase?.ScreenInformation);
        Assert.Equal(500, supportCase!.ScreenInformation!.Width);
        Assert.Equal(300, supportCase.ScreenInformation.Height);

        var updated = await _fixture.LoadConversationAsync(conversation.Id);
        Assert.Equal(ConversationStage.AskPanelLabelPhotos, updated.CurrentStage);
    }

    [Fact]
    public async Task Multi_Slot_Message_Fills_Screen_And_Receiver()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync();
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "Merhaba");
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "500 cm x 300 cm ve 8 receiver");

        var supportCase = await _fixture.LoadCaseAsync(conversation.Id);
        Assert.Equal(500, supportCase!.ScreenInformation!.Width);
        Assert.Equal(8, supportCase.ReceiverInformation!.Count);
    }

    [Fact]
    public async Task Partial_Width_Prompts_For_Height()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync();
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "Merhaba");

        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "Yatay 500 cm");

        var supportCase = await _fixture.LoadCaseAsync(conversation.Id);
        Assert.Equal(500, supportCase!.ScreenInformation!.Width);
        Assert.Equal(0, supportCase.ScreenInformation.Height);
    }

    [Fact]
    public async Task Meter_Units_Converted_To_Cm_In_Database()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync();
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "Merhaba");
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "5 metre x 3 metre");

        var supportCase = await _fixture.LoadCaseAsync(conversation.Id);
        Assert.NotNull(supportCase?.ScreenInformation);
        Assert.Equal(500, supportCase!.ScreenInformation!.Width);
        Assert.Equal(300, supportCase.ScreenInformation.Height);
        Assert.Equal(DimensionUnit.Cm, supportCase.ScreenInformation.Unit);
    }

    [Fact]
    public async Task Multiple_Image_Messages_Persist_All_Panel_Photos()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync();
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "Merhaba");
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "500 cm x 300 cm");

        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-img-1", MessageType.Image, null);
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-img-2", MessageType.Image, null);

        var supportCase = await _fixture.LoadCaseAsync(conversation.Id);
        Assert.Equal(2, supportCase!.PanelLabelPhotos.Count);
    }

    [Fact]
    public async Task Human_Mode_Blocks_Workflow_Processing()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync();
        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var entity = await db.Conversations.SingleAsync(c => c.Id == conversation.Id);
            entity.BotMode = BotMode.Human;
            entity.Status = ConversationStatus.InHumanSupport;
            await db.SaveChangesAsync();
        }

        _fixture.WhatsApp.SentTexts.Clear();
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "500 cm x 300 cm");

        Assert.Empty(_fixture.WhatsApp.SentTexts);
        var supportCase = await _fixture.LoadCaseAsync(conversation.Id);
        Assert.Null(supportCase?.ScreenInformation);
    }

    [Fact]
    public async Task Full_Workflow_Hands_Off_To_Human()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync();
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "Merhaba");
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "500 cm x 300 cm");
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-img", MessageType.Image, null);
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "8 tane");
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "evet");
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "123456789");

        var updated = await _fixture.LoadConversationAsync(conversation.Id);
        Assert.Equal(BotMode.Human, updated.BotMode);
        Assert.Equal(ConversationStatus.InHumanSupport, updated.Status);
        Assert.Equal(ConversationStage.HumanHandoff, updated.CurrentStage);

        var supportCase = await _fixture.LoadCaseAsync(conversation.Id);
        Assert.NotNull(supportCase?.SummaryMarkdown);
        Assert.Contains("500", supportCase!.SummaryMarkdown);
    }

    [Fact]
    public async Task Screen_Dimensions_Followed_By_AnyDesk_Preserves_Dimensions_And_Never_Regresses_To_AskScreenSize()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync();

        // 1. Customer initiates request
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "Led ekran kurulumu yapmak istiyorum");

        // 2. Customer provides natural language dimensions
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "192cm yükseklik 160 cm genişlik");

        var caseAfterDims = await _fixture.LoadCaseAsync(conversation.Id);
        Assert.NotNull(caseAfterDims?.ScreenInformation);
        Assert.Equal(160, caseAfterDims!.ScreenInformation!.Width);
        Assert.Equal(192, caseAfterDims.ScreenInformation.Height);

        var convAfterDims = await _fixture.LoadConversationAsync(conversation.Id);
        Assert.NotEqual(ConversationStage.AskScreenSize, convAfterDims.CurrentStage);

        // 3. Customer provides AnyDesk address
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "123 456 789");

        var caseAfterAnyDesk = await _fixture.LoadCaseAsync(conversation.Id);
        Assert.NotNull(caseAfterAnyDesk?.ScreenInformation);
        Assert.Equal(160, caseAfterAnyDesk!.ScreenInformation!.Width);
        Assert.Equal(192, caseAfterAnyDesk.ScreenInformation.Height);
        Assert.NotNull(caseAfterAnyDesk.AnyDeskInformation);
        Assert.Equal("123456789", caseAfterAnyDesk.AnyDeskInformation!.AnyDeskAddress);

        var convAfterAnyDesk = await _fixture.LoadConversationAsync(conversation.Id);
        Assert.NotEqual(ConversationStage.AskScreenSize, convAfterAnyDesk.CurrentStage);
    }

    [Fact]
    public async Task Compact_Dimensions_192x160_Persists_Width_And_Height()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync();

        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "192x160");

        var supportCase = await _fixture.LoadCaseAsync(conversation.Id);
        Assert.NotNull(supportCase?.ScreenInformation);
        Assert.Equal(192, supportCase!.ScreenInformation!.Width);
        Assert.Equal(160, supportCase.ScreenInformation.Height);

        var conv = await _fixture.LoadConversationAsync(conversation.Id);
        Assert.NotEqual(ConversationStage.AskScreenSize, conv.CurrentStage);
    }
}

public sealed class RecordingWhatsAppClient : IWhatsAppClient
{
    public List<string> SentTexts { get; } = [];
    private readonly Dictionary<string, (string Mime, byte[] Content)> _downloads = new(StringComparer.Ordinal);

    /// <summary>When true, send/download throw as if the connector is offline.</summary>
    public bool SimulateConnectorUnavailable { get; set; }

    public void SetDownload(string externalMessageId, string mimeType, byte[] content) =>
        _downloads[externalMessageId] = (mimeType, content);

    public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<WhatsAppConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(WhatsAppConnectionStatus.Connected);

    public Task<string?> SendTextMessageAsync(string toPhoneE164, string body, CancellationToken cancellationToken = default)
    {
        if (SimulateConnectorUnavailable)
        {
            throw new InvalidOperationException("Connector offline (simulated).");
        }

        SentTexts.Add(body);
        return Task.FromResult<string?>($"fake-{Guid.NewGuid():N}");
    }

    public Task<string?> SendImageMessageAsync(
        string toPhoneE164,
        Stream image,
        string contentType,
        string? caption = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>($"fake-img-{Guid.NewGuid():N}");

    public Task SendComposingAsync(string toPhoneE164, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SendPausedPresenceAsync(string toPhoneE164, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<WhatsAppMediaDownloadResult> DownloadMediaAsync(string mediaId, CancellationToken cancellationToken = default)
    {
        if (!_downloads.TryGetValue(mediaId, out var item))
        {
            throw new InvalidOperationException("media_not_found");
        }

        return Task.FromResult(new WhatsAppMediaDownloadResult(mediaId, item.Mime, item.Content));
    }
}
