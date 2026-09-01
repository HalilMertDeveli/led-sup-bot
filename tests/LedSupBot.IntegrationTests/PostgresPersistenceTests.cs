using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace LedSupBot.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("led_sup_bot_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;

[Collection("postgres")]
public sealed class PostgresPersistenceTests
{
    private readonly PostgresFixture _fixture;

    public PostgresPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Can_Create_Customer()
    {
        await using var db = _fixture.CreateDbContext();
        var customer = new Customer { PhoneNumber = $"+90555{Random.Shared.Next(1000000, 9999999)}" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var loaded = await db.Customers.SingleAsync(x => x.Id == customer.Id);
        Assert.Equal(customer.PhoneNumber, loaded.PhoneNumber);
    }

    [Fact]
    public async Task Can_Create_Conversation_Linked_To_Customer()
    {
        await using var db = _fixture.CreateDbContext();
        var customer = new Customer { PhoneNumber = $"+90556{Random.Shared.Next(1000000, 9999999)}" };
        var conversation = new Conversation
        {
            Customer = customer,
            Status = ConversationStatus.Active,
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.Greeting
        };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        var loaded = await db.Conversations.Include(x => x.Customer).SingleAsync(x => x.Id == conversation.Id);
        Assert.Equal(customer.Id, loaded.CustomerId);
        Assert.Equal(BotMode.Bot, loaded.BotMode);
        Assert.Equal(ConversationStage.Greeting, loaded.CurrentStage);
    }

    [Fact]
    public async Task Can_Create_Messages()
    {
        await using var db = _fixture.CreateDbContext();
        var conversation = await SeedConversationAsync(db);

        db.Messages.AddRange(
            new Message
            {
                ConversationId = conversation.Id,
                Direction = MessageDirection.Inbound,
                MessageType = MessageType.Text,
                Content = "Merhaba",
                SentBy = MessageSender.Customer,
                ExternalMessageId = $"wamid.{Guid.NewGuid():N}"
            },
            new Message
            {
                ConversationId = conversation.Id,
                Direction = MessageDirection.Outbound,
                MessageType = MessageType.Image,
                Content = null,
                SentBy = MessageSender.Bot,
                StorageKey = "outbound/config-example.png"
            });
        await db.SaveChangesAsync();

        var count = await db.Messages.CountAsync(x => x.ConversationId == conversation.Id);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task ExternalMessageId_Uniqueness_Enforced()
    {
        await using var db = _fixture.CreateDbContext();
        var conversation = await SeedConversationAsync(db);
        var externalId = $"wamid.dup.{Guid.NewGuid():N}";

        db.Messages.Add(new Message
        {
            ConversationId = conversation.Id,
            Direction = MessageDirection.Inbound,
            MessageType = MessageType.Text,
            Content = "one",
            ExternalMessageId = externalId
        });
        await db.SaveChangesAsync();

        db.Messages.Add(new Message
        {
            ConversationId = conversation.Id,
            Direction = MessageDirection.Inbound,
            MessageType = MessageType.Text,
            Content = "two",
            ExternalMessageId = externalId
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Can_Create_TechnicalSupportCase_And_Related_Slots()
    {
        await using var db = _fixture.CreateDbContext();
        var conversation = await SeedConversationAsync(db);

        var supportCase = new TechnicalSupportCase
        {
            ConversationId = conversation.Id,
            Status = SupportCaseStatus.CollectingInformation,
            ScreenInformation = new ScreenInformation
            {
                Width = 500,
                Height = 300,
                Unit = DimensionUnit.Cm,
                RawUtterance = "500x300"
            },
            ReceiverInformation = new ReceiverInformation
            {
                Count = 8,
                Notes = null
            },
            ConfigConnection = new ConfigConnectionInfo
            {
                Status = ConfigConnectionStatus.Requested,
                StatusChangedAt = DateTimeOffset.UtcNow,
                ExampleImageSent = true
            },
            AnyDeskInformation = new AnyDeskInformation
            {
                AnyDeskAddress = "123456789",
                CollectedAt = DateTimeOffset.UtcNow
            },
            PanelLabelPhotos =
            {
                new PanelLabelPhoto
                {
                    StorageKey = $"cases/{Guid.NewGuid():N}/label-1.jpg",
                    ContentType = "image/jpeg",
                    SortOrder = 1,
                    AnalysisStatus = MediaAnalysisStatus.NotStarted
                },
                new PanelLabelPhoto
                {
                    StorageKey = $"cases/{Guid.NewGuid():N}/label-2.jpg",
                    ContentType = "image/jpeg",
                    SortOrder = 2
                }
            }
        };

        db.TechnicalSupportCases.Add(supportCase);
        await db.SaveChangesAsync();

        var loaded = await db.TechnicalSupportCases
            .Include(x => x.ScreenInformation)
            .Include(x => x.ReceiverInformation)
            .Include(x => x.ConfigConnection)
            .Include(x => x.AnyDeskInformation)
            .Include(x => x.PanelLabelPhotos)
            .SingleAsync(x => x.Id == supportCase.Id);

        Assert.Equal(500, loaded.ScreenInformation!.Width);
        Assert.Equal(DimensionUnit.Cm, loaded.ScreenInformation.Unit);
        Assert.Equal(8, loaded.ReceiverInformation!.Count);
        Assert.Equal(ConfigConnectionStatus.Requested, loaded.ConfigConnection!.Status);
        Assert.Equal("123456789", loaded.AnyDeskInformation!.AnyDeskAddress);
        Assert.Equal(2, loaded.PanelLabelPhotos.Count);
    }

    [Fact]
    public async Task Can_Store_BotMode_And_ConversationStage()
    {
        await using var db = _fixture.CreateDbContext();
        var conversation = await SeedConversationAsync(db);
        conversation.BotMode = BotMode.Human;
        conversation.CurrentStage = ConversationStage.HumanHandoff;
        conversation.Status = ConversationStatus.InHumanSupport;
        await db.SaveChangesAsync();

        var loaded = await db.Conversations.SingleAsync(x => x.Id == conversation.Id);
        Assert.Equal(BotMode.Human, loaded.BotMode);
        Assert.Equal(ConversationStage.HumanHandoff, loaded.CurrentStage);
    }

    [Fact]
    public async Task Relationships_And_ForeignKeys_Work()
    {
        await using var db = _fixture.CreateDbContext();
        var customer = new Customer { PhoneNumber = $"+90557{Random.Shared.Next(1000000, 9999999)}" };
        var conversation = new Conversation { Customer = customer, CurrentStage = ConversationStage.AskAnyDesk };
        var supportCase = new TechnicalSupportCase
        {
            Conversation = conversation,
            Status = SupportCaseStatus.Open
        };
        var message = new Message
        {
            Conversation = conversation,
            Direction = MessageDirection.Inbound,
            MessageType = MessageType.Text,
            Content = "test",
            ExternalMessageId = $"wamid.rel.{Guid.NewGuid():N}"
        };
        var extraction = new AiExtractionResult
        {
            Conversation = conversation,
            Message = message,
            ExtractionType = AiExtractionType.SlotExtraction,
            ResultJson = """{"slots":{"receiverCount":8}}""",
            Confidence = 0.9
        };

        db.AddRange(customer, conversation, supportCase, message, extraction);
        await db.SaveChangesAsync();

        var reloadedCase = await db.TechnicalSupportCases
            .Include(x => x.Conversation)
            .ThenInclude(x => x.Customer)
            .SingleAsync(x => x.Id == supportCase.Id);

        Assert.Equal(customer.PhoneNumber, reloadedCase.Conversation.Customer.PhoneNumber);
        Assert.Equal(1, await db.Messages.CountAsync(x => x.ConversationId == conversation.Id));
        Assert.Equal(1, await db.AiExtractionResults.CountAsync(x => x.ConversationId == conversation.Id));
    }

    [Fact]
    public async Task Customer_PhoneNumber_Is_Unique()
    {
        await using var db = _fixture.CreateDbContext();
        var phone = $"+90558{Random.Shared.Next(1000000, 9999999)}";
        db.Customers.Add(new Customer { PhoneNumber = phone });
        await db.SaveChangesAsync();

        db.Customers.Add(new Customer { PhoneNumber = phone });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Migration_Applied_Creates_Expected_Tables()
    {
        await using var db = _fixture.CreateDbContext();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name IN (
                'customers','conversations','messages','technical_support_cases',
                'screen_informations','receiver_informations','panel_label_photos',
                'config_connections','anydesk_informations','ai_extraction_results'
              );
            """;
        var count = Convert.ToInt32(await command.ExecuteScalarAsync());
        Assert.Equal(10, count);
    }

    private static async Task<Conversation> SeedConversationAsync(ApplicationDbContext db)
    {
        var conversation = new Conversation
        {
            Customer = new Customer { PhoneNumber = $"+90559{Random.Shared.Next(1000000, 9999999)}" },
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.Start
        };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        return conversation;
    }
}
