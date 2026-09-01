using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using LedSupBot.Infrastructure.Persistence;
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
    public IServiceProvider Services { get; private set; } = null!;
    public RecordingWhatsAppClient WhatsApp { get; private set; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<RecordingWhatsAppClient>();
        services.AddSingleton<IWhatsAppClient>(sp => sp.GetRequiredService<RecordingWhatsAppClient>());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = ConnectionString,
                ["Workflow:AutoReplyEnabled"] = "false",
                ["Workflow:ConfigExampleImagePath"] = "",
                ["WhatsAppConnector:Enabled"] = "false",
            })
            .Build();

        LedSupBot.Application.DependencyInjection.AddApplication(services);
        LedSupBot.Infrastructure.DependencyInjection.AddInfrastructure(services, configuration);

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
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "500x300");

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
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "500x300 ve 8 receiver");

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
    public async Task Meter_Units_Do_Not_Complete_Screen_Without_Confirmation()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync();
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "Merhaba");
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "5 metre x 3 metre");

        var supportCase = await _fixture.LoadCaseAsync(conversation.Id);
        Assert.Null(supportCase?.ScreenInformation);
    }

    [Fact]
    public async Task Multiple_Image_Messages_Persist_All_Panel_Photos()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync();
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "Merhaba");
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "500x300");

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
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "500x300");

        Assert.Empty(_fixture.WhatsApp.SentTexts);
        var supportCase = await _fixture.LoadCaseAsync(conversation.Id);
        Assert.Null(supportCase?.ScreenInformation);
    }

    [Fact]
    public async Task Full_Workflow_Hands_Off_To_Human()
    {
        var (conversation, customer) = await _fixture.SeedConversationAsync();
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "Merhaba");
        await _fixture.ProcessAsync(conversation.Id, customer.PhoneNumber, $"wf-{Guid.NewGuid():N}", MessageType.Text, "500x300");
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
}

public sealed class RecordingWhatsAppClient : IWhatsAppClient
{
    public List<string> SentTexts { get; } = [];

    public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<WhatsAppConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(WhatsAppConnectionStatus.Connected);

    public Task<string?> SendTextMessageAsync(string toPhoneE164, string body, CancellationToken cancellationToken = default)
    {
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

    public Task<WhatsAppMediaDownloadResult> DownloadMediaAsync(string mediaId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
