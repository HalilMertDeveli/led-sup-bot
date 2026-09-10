using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Takeover;
using LedSupBot.Application.WhatsApp;
using LedSupBot.Application.Workflow;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace LedSupBot.IntegrationTests;

public sealed class TakeoverFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("led_sup_bot_takeover")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();
    public IServiceProvider Services { get; private set; } = null!;
    public BlockingAiService Ai { get; private set; } = new();
    public RecordingWhatsAppClient WhatsApp { get; private set; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = ConnectionString,
                ["Workflow:AutoReplyEnabled"] = "true",
                ["Workflow:InboundCoalescing:Enabled"] = "false",
                ["Workflow:NaturalTiming:Enabled"] = "false",
                ["Workflow:ConfigExampleImagePath"] = "",
                ["WhatsAppConnector:Enabled"] = "false",
                ["MediaStorage:RootPath"] = Path.Combine(Path.GetTempPath(), "led-sup-bot-takeover-media", Guid.NewGuid().ToString("N")),
                ["MediaStorage:MaxFileSizeBytes"] = "10485760",
            })
            .Build();

        LedSupBot.Application.DependencyInjection.AddApplication(services);
        LedSupBot.Infrastructure.DependencyInjection.AddInfrastructure(services, configuration);

        Ai = new BlockingAiService();
        services.AddSingleton(Ai);
        services.AddSingleton<IAiService>(sp => sp.GetRequiredService<BlockingAiService>());
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
    }

    public async Task<(Conversation Conversation, Customer Customer)> SeedConversationAsync(BotMode mode = BotMode.Bot)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var phone = $"90555{Random.Shared.Next(1_000_000, 9_999_999)}";
        var customer = new Customer { PhoneNumber = phone };
        db.Customers.Add(customer);
        var conversation = new Conversation
        {
            CustomerId = customer.Id,
            Status = mode == BotMode.Human ? ConversationStatus.InHumanSupport : ConversationStatus.Active,
            BotMode = mode,
            CurrentStage = mode == BotMode.Human ? ConversationStage.HumanHandoff : ConversationStage.AskScreenSize,
        };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        return (conversation, customer);
    }

    public async Task SeedPartialCaseAsync(Guid conversationId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var supportCase = new TechnicalSupportCase
        {
            ConversationId = conversationId,
            Status = SupportCaseStatus.CollectingInformation,
        };
        db.TechnicalSupportCases.Add(supportCase);
        await db.SaveChangesAsync();

        db.ScreenInformations.Add(new ScreenInformation
        {
            TechnicalSupportCaseId = supportCase.Id,
            Width = 500,
            Height = 300,
            Unit = DimensionUnit.Cm,
            RawUtterance = "500 cm x 300 cm",
        });
        db.PanelLabelPhotos.Add(new PanelLabelPhoto
        {
            TechnicalSupportCaseId = supportCase.Id,
            StorageKey = $"cases/{supportCase.Id:N}/photo1.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 1000,
            SortOrder = 1,
        });
        db.ReceiverInformations.Add(new ReceiverInformation
        {
            TechnicalSupportCaseId = supportCase.Id,
            Count = 8,
            ReceiverModel = ReceiverModel.E120,
            Model = "E120",
            RawUtterance = "8",
        });
        supportCase.PlayerType = PlayerType.Playbox;
        db.ConfigConnections.Add(new ConfigConnectionInfo
        {
            TechnicalSupportCaseId = supportCase.Id,
            Status = ConfigConnectionStatus.Confirmed,
            ExampleImageSent = true,
        });
        await db.SaveChangesAsync();
    }
}

[CollectionDefinition("takeover")]
public sealed class TakeoverCollection : ICollectionFixture<TakeoverFixture>;

[Collection("takeover")]
public sealed class TakeoverIntegrationTests
{
    private readonly TakeoverFixture _fixture;

    public TakeoverIntegrationTests(TakeoverFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Technician_Message_Sets_Human_And_HumanHandoff()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync();
        await using var scope = _fixture.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IWhatsAppInboundEventHandler>();

        await handler.HandleAsync(new WhatsAppInboundEvent
        {
            EventType = "IncomingMessage",
            ExternalMessageId = $"tech-{Guid.NewGuid():N}",
            RemotePhoneNumber = customer.PhoneNumber,
            FromMe = true,
            BotOriginated = false,
            MessageType = "Text",
            TextContent = "Merhaba ben Halil",
            TextPresent = true,
        });

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var updated = await db.Conversations.SingleAsync(c => c.Id == conversation.Id);
        Assert.Equal(BotMode.Human, updated.BotMode);
        Assert.Equal(ConversationStatus.InHumanSupport, updated.Status);
        Assert.Equal(ConversationStage.HumanHandoff, updated.CurrentStage);
    }

    [Fact]
    public async Task Customer_Message_In_Human_Is_Persisted_Without_Reply()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync(BotMode.Human);
        _fixture.WhatsApp.SentTexts.Clear();
        _fixture.Ai.Reset();

        await using var scope = _fixture.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IWhatsAppInboundEventHandler>();
        await handler.HandleAsync(new WhatsAppInboundEvent
        {
            EventType = "IncomingMessage",
            ExternalMessageId = $"cust-human-{Guid.NewGuid():N}",
            RemotePhoneNumber = customer.PhoneNumber,
            FromMe = false,
            MessageType = "Text",
            TextContent = "500 cm x 300 cm",
            TextPresent = true,
        });

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Messages.CountAsync(m => m.ConversationId == conversation.Id && m.SentBy == MessageSender.Customer));
        Assert.Empty(_fixture.WhatsApp.SentTexts);
        Assert.Equal(0, _fixture.Ai.CallCount);
    }

    [Fact]
    public async Task Customer_Message_In_Paused_Is_Persisted_Without_Reply()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync(BotMode.Paused);
        _fixture.WhatsApp.SentTexts.Clear();
        _fixture.Ai.Reset();

        await using var scope = _fixture.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IWhatsAppInboundEventHandler>();
        await handler.HandleAsync(new WhatsAppInboundEvent
        {
            EventType = "IncomingMessage",
            ExternalMessageId = $"cust-pause-{Guid.NewGuid():N}",
            RemotePhoneNumber = customer.PhoneNumber,
            FromMe = false,
            MessageType = "Text",
            TextContent = "hello",
            TextPresent = true,
        });

        Assert.Empty(_fixture.WhatsApp.SentTexts);
        Assert.Equal(0, _fixture.Ai.CallCount);
    }

    [Fact]
    public async Task Pause_And_Resume_Are_Idempotent()
    {
        var (conversation, _) = await _fixture.SeedConversationAsync();
        await using var scope = _fixture.Services.CreateAsyncScope();
        var takeover = scope.ServiceProvider.GetRequiredService<IConversationTakeoverService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var pause1 = await takeover.PauseAsync(conversation.Id);
        var pause2 = await takeover.PauseAsync(conversation.Id);
        Assert.True(pause1.Changed);
        Assert.False(pause2.Changed);

        await _fixture.SeedPartialCaseAsync(conversation.Id);
        var resume1 = await takeover.ResumeBotAsync(conversation.Id);
        var resume2 = await takeover.ResumeBotAsync(conversation.Id);
        Assert.True(resume1.Changed);
        Assert.False(resume2.Changed);
        Assert.Equal(ConversationStage.AskAnyDesk, resume1.CurrentStage);

        Assert.Equal(2, await db.BotModeChangeAudits.CountAsync(a => a.ConversationId == conversation.Id));
    }

    [Fact]
    public async Task Resume_Does_Not_Reset_Completed_Slots()
    {
        var (conversation, _) = await _fixture.SeedConversationAsync(BotMode.Human);
        await _fixture.SeedPartialCaseAsync(conversation.Id);

        await using var scope = _fixture.Services.CreateAsyncScope();
        var takeover = scope.ServiceProvider.GetRequiredService<IConversationTakeoverService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await takeover.ResumeBotAsync(conversation.Id);

        db.ChangeTracker.Clear();
        var supportCase = await db.TechnicalSupportCases
            .Include(c => c.ScreenInformation)
            .Include(c => c.ReceiverInformation)
            .AsNoTracking()
            .SingleAsync(c => c.ConversationId == conversation.Id);

        Assert.Equal(500, supportCase.ScreenInformation!.Width);
        Assert.Equal(8, supportCase.ReceiverInformation!.Count);
        Assert.Equal(ConversationStage.AskAnyDesk, (await db.Conversations.AsNoTracking().SingleAsync(c => c.Id == conversation.Id)).CurrentStage);
    }

    [Fact]
    public async Task Takeover_Audit_Is_Written()
    {
        var (conversation, _) = await _fixture.SeedConversationAsync();
        await using var scope = _fixture.Services.CreateAsyncScope();
        var takeover = scope.ServiceProvider.GetRequiredService<IConversationTakeoverService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await takeover.TakeOverAsync(conversation.Id, TakeoverChangeSource.AdminTakeover);
        await takeover.PauseAsync(conversation.Id);
        await takeover.ResumeBotAsync(conversation.Id);

        var audits = await db.BotModeChangeAudits.Where(a => a.ConversationId == conversation.Id).ToListAsync();
        Assert.Equal(3, audits.Count);
        Assert.Contains(audits, a => a.Source == TakeoverChangeSource.AdminTakeover);
        Assert.Contains(audits, a => a.Source == TakeoverChangeSource.AdminPause);
        Assert.Contains(audits, a => a.Source == TakeoverChangeSource.AdminResume);
    }

    [Fact]
    public async Task OpenAi_Running_Then_Takeover_Does_Not_Send_Outbound()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync();
        _fixture.WhatsApp.SentTexts.Clear();
        _fixture.Ai.Reset();
        _fixture.Ai.BlockNextCall();

        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var workflow = scope.ServiceProvider.GetRequiredService<IConversationWorkflowService>();
        var takeover = scope.ServiceProvider.GetRequiredService<IConversationTakeoverService>();

        var message = new Message
        {
            ConversationId = conversation.Id,
            Direction = MessageDirection.Inbound,
            MessageType = MessageType.Text,
            Content = "Abi ekranÄ±mÄ±z 500'e 300, arkada 8 tane kart var",
            ExternalMessageId = $"race-{Guid.NewGuid():N}",
            DeliveryStatus = DeliveryStatus.Delivered,
            SentBy = MessageSender.Customer,
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();

        var workflowTask = workflow.ProcessCustomerInboundAsync(
            new WorkflowInboundMessage(
                conversation.Id,
                customer.PhoneNumber,
                message.Id,
                message.ExternalMessageId!,
                MessageType.Text,
                message.Content));

        await _fixture.Ai.WaitUntilBlockedAsync();
        await takeover.TakeOverAsync(conversation.Id, TakeoverChangeSource.TechnicianMessage);
        _fixture.Ai.ReleaseBlockedCall();

        await workflowTask;

        Assert.Empty(_fixture.WhatsApp.SentTexts);
    }
}

public sealed class BlockingAiService : IAiService
{
    private TaskCompletionSource? _blockGate;
    private TaskCompletionSource? _blockedSignal;
    public int CallCount { get; private set; }

    public void Reset()
    {
        CallCount = 0;
        _blockGate = null;
        _blockedSignal = null;
    }

    public void BlockNextCall()
    {
        _blockGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _blockedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public Task WaitUntilBlockedAsync() =>
        _blockedSignal?.Task ?? Task.CompletedTask;

    public void ReleaseBlockedCall() => _blockGate?.TrySetResult();

    public async Task<AiStructuredExtraction> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default)
    {
        CallCount++;
        _blockedSignal?.TrySetResult();
        if (_blockGate is not null)
        {
            await _blockGate.Task.WaitAsync(cancellationToken);
        }

        return new AiStructuredExtraction(
            ConversationIntent.WorkflowInformation,
            new ScreenSlotCandidate(500, 300, null, null, 0.98, false),
            new ReceiverSlotCandidate(8, 0.97),
            null, null, []);
    }
}
