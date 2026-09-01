using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace LedSupBot.IntegrationTests;

public sealed class AiWorkflowFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("led_sup_bot_ai")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();
    public IServiceProvider Services { get; private set; } = null!;
    public RecordingAiService Ai { get; private set; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = ConnectionString,
                ["Workflow:AutoReplyEnabled"] = "false",
                ["Workflow:ConfigExampleImagePath"] = "",
                ["AiExtraction:ConfidenceThreshold"] = "0.85",
                ["AiExtraction:PreferDeterministicFirst"] = "true",
                ["WhatsAppConnector:Enabled"] = "false",
                ["MediaStorage:RootPath"] = Path.Combine(Path.GetTempPath(), "led-sup-bot-ai-media", Guid.NewGuid().ToString("N")),
                ["MediaStorage:MaxFileSizeBytes"] = "10485760",
            })
            .Build();

        LedSupBot.Application.DependencyInjection.AddApplication(services);
        LedSupBot.Infrastructure.DependencyInjection.AddInfrastructure(services, configuration);

        services.AddSingleton<RecordingWhatsAppClient>();
        services.AddSingleton<IWhatsAppClient>(sp => sp.GetRequiredService<RecordingWhatsAppClient>());

        Ai = new RecordingAiService();
        services.AddSingleton(Ai);
        services.AddSingleton<IAiService>(sp => sp.GetRequiredService<RecordingAiService>());

        Services = services.BuildServiceProvider();

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
            .FirstOrDefaultAsync(c => c.ConversationId == conversationId);
    }
}

[CollectionDefinition("ai-workflow")]
public sealed class AiWorkflowCollection : ICollectionFixture<AiWorkflowFixture>;

[Collection("ai-workflow")]
public sealed class AiWorkflowIntegrationTests
{
    private readonly AiWorkflowFixture _fixture;

    public AiWorkflowIntegrationTests(AiWorkflowFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Ai_Supplements_Multi_Slot_Message_When_Deterministic_Misses_Receiver()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync();
        _fixture.Ai.Reset();
        _fixture.Ai.NextResponse = new AiStructuredExtraction(
            CustomerMessageIntent.TechnicalSupport,
            new ScreenSlotCandidate(500, 300, null, null, 0.98, false),
            new ReceiverSlotCandidate(8, 0.97),
            null, null, []);

        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"ai-{Guid.NewGuid():N}", MessageType.Text, "Merhaba");
        await _fixture.ProcessAsync(
            conversation.Id,
            customer.PhoneNumber,
            $"ai-{Guid.NewGuid():N}",
            MessageType.Text,
            "Abi ekranımız 500'e 300, arkada 8 tane kart var");

        Assert.True(_fixture.Ai.CallCount > 0);
        var supportCase = await _fixture.LoadCaseAsync(conversation.Id);
        Assert.Equal(500, supportCase!.ScreenInformation!.Width);
        Assert.Equal(8, supportCase.ReceiverInformation!.Count);
    }

    [Fact]
    public async Task Ai_Low_Confidence_Does_Not_Persist_Ambiguous_5x3()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync();
        _fixture.Ai.Reset();
        _fixture.Ai.NextResponse = new AiStructuredExtraction(
            CustomerMessageIntent.TechnicalSupport,
            new ScreenSlotCandidate(null, null, null, null, 0.5, true),
            null, null, null,
            ["unit ambiguous"]);

        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"ai-{Guid.NewGuid():N}", MessageType.Text, "Merhaba");
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"ai-{Guid.NewGuid():N}", MessageType.Text, "5x3");

        var supportCase = await _fixture.LoadCaseAsync(conversation.Id);
        Assert.Null(supportCase?.ScreenInformation);
    }

    [Fact]
    public async Task Ai_Config_Confirmation_Persisted_After_Validation()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync();
        _fixture.Ai.Reset();
        _fixture.Ai.NextResponse = new AiStructuredExtraction(
            CustomerMessageIntent.Confirmation,
            null, null,
            new ConfigSlotCandidate(true, true, 0.94),
            null, []);

        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"ai-{Guid.NewGuid():N}", MessageType.Text, "Merhaba");
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"ai-{Guid.NewGuid():N}", MessageType.Text, "500 cm x 300 cm");
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-img", MessageType.Image, null);
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"ai-{Guid.NewGuid():N}", MessageType.Text, "8 tane");
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"ai-{Guid.NewGuid():N}", MessageType.Text, "evet bağladım");

        var supportCase = await _fixture.LoadCaseAsync(conversation.Id);
        Assert.Equal(ConfigConnectionStatus.Confirmed, supportCase!.ConfigConnection!.Status);
    }

    [Fact]
    public async Task Human_Mode_Blocks_Ai_And_Workflow()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync();
        _fixture.Ai.Reset();
        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var entity = await db.Conversations.SingleAsync(c => c.Id == conversation.Id);
            entity.BotMode = BotMode.Human;
            entity.Status = ConversationStatus.InHumanSupport;
            await db.SaveChangesAsync();
        }

        _fixture.Ai.Reset();
        _fixture.Ai.NextResponse = new AiStructuredExtraction(
            CustomerMessageIntent.TechnicalSupport,
            new ScreenSlotCandidate(500, 300, null, null, 0.99, false),
            null, null, null, []);

        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"ai-{Guid.NewGuid():N}", MessageType.Text, "500 cm x 300 cm");
        Assert.Equal(0, _fixture.Ai.CallCount);
    }

    [Fact]
    public async Task Ai_Audit_Record_Persisted_Without_Sensitive_Data()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync();
        _fixture.Ai.Reset();
        _fixture.Ai.NextResponse = new AiStructuredExtraction(
            CustomerMessageIntent.TechnicalSupport,
            null, null, null,
            new AnyDeskSlotCandidate("123456789", 0.99),
            []);

        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"ai-{Guid.NewGuid():N}", MessageType.Text, "Merhaba");
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"ai-{Guid.NewGuid():N}", MessageType.Text, "123456789");

        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audit = await db.AiExtractionResults.FirstOrDefaultAsync(a => a.ConversationId == conversation.Id);
        Assert.NotNull(audit);
        Assert.DoesNotContain("123456789", audit!.ResultJson);
        Assert.DoesNotContain(customer.PhoneNumber, audit.ResultJson);
    }
}

public sealed class RecordingAiService : IAiService
{
    public AiStructuredExtraction NextResponse { get; set; } = new(
        CustomerMessageIntent.Unknown, null, null, null, null, []);

    public int CallCount { get; private set; }

    public void Reset() => CallCount = 0;

    public Task<AiStructuredExtraction> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(NextResponse);
    }
}
