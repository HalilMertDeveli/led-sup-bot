using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Media;
using LedSupBot.Application.WhatsApp;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using LedSupBot.Infrastructure.Persistence;
using LedSupBot.UnitTests.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace LedSupBot.IntegrationTests;

public sealed class MediaFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("led_sup_bot_media")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();
    public string MediaRoot { get; private set; } = string.Empty;
    public IServiceProvider Services { get; private set; } = null!;
    public ConfigurableWhatsAppClient WhatsApp { get; private set; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        MediaRoot = Path.Combine(Path.GetTempPath(), "led-sup-bot-media-int", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(MediaRoot);

        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = ConnectionString,
                ["Workflow:AutoReplyEnabled"] = "false",
                ["Workflow:InboundCoalescing:Enabled"] = "false",
                ["Workflow:NaturalTiming:Enabled"] = "false",
                ["Workflow:ConfigExampleImagePath"] = "",
                ["WhatsAppConnector:Enabled"] = "false",
                ["MediaStorage:RootPath"] = MediaRoot,
                ["MediaStorage:MaxFileSizeBytes"] = "1048576",
            })
            .Build();

        LedSupBot.Application.DependencyInjection.AddApplication(services);
        LedSupBot.Infrastructure.DependencyInjection.AddInfrastructure(services, configuration);

        services.AddSingleton<ConfigurableWhatsAppClient>();
        services.AddSingleton<IWhatsAppClient>(sp => sp.GetRequiredService<ConfigurableWhatsAppClient>());

        Services = services.BuildServiceProvider();
        WhatsApp = Services.GetRequiredService<ConfigurableWhatsAppClient>();

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
            // Best effort.
        }
    }

    public async Task<(Conversation Conversation, Customer Customer, TechnicalSupportCase Case)> SeedCaseAsync(string? phone = null)
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
            CurrentStage = ConversationStage.AskPanelLabelPhotos,
        };
        db.Conversations.Add(conversation);
        var supportCase = new TechnicalSupportCase
        {
            ConversationId = conversation.Id,
            Status = SupportCaseStatus.CollectingInformation,
        };
        db.TechnicalSupportCases.Add(supportCase);
        db.ScreenInformations.Add(new ScreenInformation
        {
            TechnicalSupportCaseId = supportCase.Id,
            Width = 500,
            Height = 300,
            Unit = DimensionUnit.Cm,
            RawUtterance = "500 cm x 300 cm",
        });
        await db.SaveChangesAsync();
        return (conversation, customer, supportCase);
    }

    public async Task<Message> PersistInboundImageMessageAsync(
        Guid conversationId,
        string externalId,
        string? caption = null)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var message = new Message
        {
            ConversationId = conversationId,
            Direction = MessageDirection.Inbound,
            MessageType = MessageType.Image,
            Content = caption,
            ExternalMessageId = externalId,
            DeliveryStatus = DeliveryStatus.Delivered,
            SentBy = MessageSender.Customer,
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();
        return message;
    }

    public async Task<InboundMediaProcessResult> ProcessImageThroughPipelineAsync(
        TechnicalSupportCase supportCase,
        Message message,
        string externalId,
        byte[]? imageBytes = null,
        string mimeType = "image/png",
        bool downloadable = true,
        long? fileLength = null,
        bool configureDownload = true)
    {
        imageBytes ??= TestImageBytes.CreatePng();
        if (configureDownload)
        {
            WhatsApp.SetDownload(externalId, mimeType, imageBytes);
        }

        await using var scope = Services.CreateAsyncScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<IInboundMediaPipeline>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var trackedMessage = await db.Messages.SingleAsync(m => m.Id == message.Id);
        var trackedCase = await db.TechnicalSupportCases.SingleAsync(c => c.Id == supportCase.Id);

        return await pipeline.ProcessInboundImageAsync(
            trackedMessage,
            trackedCase,
            new WhatsAppInboundEvent
            {
                ExternalMessageId = externalId,
                MessageType = "Image",
                Media = new WhatsAppMediaMetadata
                {
                    MimeType = mimeType,
                    Downloadable = downloadable,
                    FileLength = fileLength ?? imageBytes.LongLength,
                },
            });
    }

    public async Task InvokeHandlerAsync(WhatsAppInboundEvent inboundEvent)
    {
        await using var scope = Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IWhatsAppInboundEventHandler>();
        await handler.HandleAsync(inboundEvent);
    }
}

[CollectionDefinition("media")]
public sealed class MediaCollection : ICollectionFixture<MediaFixture>;

[Collection("media")]
public sealed class MediaIntegrationTests
{
    private readonly MediaFixture _fixture;

    public MediaIntegrationTests(MediaFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Valid_Jpeg_Is_Stored_And_Referenced()
    {
        var (_, _, supportCase) = await _fixture.SeedCaseAsync();
        var externalId = $"img-jpeg-{Guid.NewGuid():N}";
        var message = await _fixture.PersistInboundImageMessageAsync(supportCase.ConversationId, externalId);
        var jpeg = TestImageBytes.CreateJpeg();

        var result = await _fixture.ProcessImageThroughPipelineAsync(supportCase, message, externalId, jpeg, "image/jpeg");
        Assert.Equal(MediaProcessOutcome.Stored, result.Outcome);
        Assert.StartsWith("cases/", result.StorageKey, StringComparison.Ordinal);

        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var photo = await db.PanelLabelPhotos.SingleAsync(p => p.TechnicalSupportCaseId == supportCase.Id);
        Assert.Equal(result.StorageKey, photo.StorageKey);
        Assert.True(File.Exists(Path.Combine(_fixture.MediaRoot, photo.StorageKey.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task Valid_Png_And_Webp_Are_Stored()
    {
        var (_, _, supportCase) = await _fixture.SeedCaseAsync();
        foreach (var (bytes, mime) in new (byte[], string)[]
        {
            (TestImageBytes.CreatePng(), "image/png"),
            (TestImageBytes.CreateWebp(), "image/webp"),
        })
        {
            var externalId = $"img-{mime.Split('/')[1]}-{Guid.NewGuid():N}";
            var message = await _fixture.PersistInboundImageMessageAsync(supportCase.ConversationId, externalId);
            var result = await _fixture.ProcessImageThroughPipelineAsync(supportCase, message, externalId, bytes, mime);
            Assert.Equal(MediaProcessOutcome.Stored, result.Outcome);
        }
    }

    [Fact]
    public async Task Unsupported_Mime_Does_Not_Create_Panel_Photo()
    {
        var (_, _, supportCase) = await _fixture.SeedCaseAsync();
        var externalId = $"img-gif-{Guid.NewGuid():N}";
        var message = await _fixture.PersistInboundImageMessageAsync(supportCase.ConversationId, externalId);
        _fixture.WhatsApp.SetDownload(externalId, "image/gif", TestImageBytes.CreateGif());

        var result = await _fixture.ProcessImageThroughPipelineAsync(
            supportCase, message, externalId, TestImageBytes.CreateGif(), "image/gif");

        Assert.Equal(MediaProcessOutcome.UnsupportedMimeType, result.Outcome);
        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.PanelLabelPhotos.Where(p => p.TechnicalSupportCaseId == supportCase.Id).ToListAsync());
    }

    [Fact]
    public async Task Corrupt_Image_Does_Not_Satisfy_Photo_Requirement()
    {
        var (_, _, supportCase) = await _fixture.SeedCaseAsync();
        var externalId = $"img-bad-{Guid.NewGuid():N}";
        var message = await _fixture.PersistInboundImageMessageAsync(supportCase.ConversationId, externalId);
        var corrupt = TestImageBytes.CorruptJpegHeader();
        _fixture.WhatsApp.SetDownload(externalId, "image/jpeg", corrupt);

        var result = await _fixture.ProcessImageThroughPipelineAsync(
            supportCase, message, externalId, corrupt, "image/jpeg");
        Assert.Equal(MediaProcessOutcome.InvalidImage, result.Outcome);
    }

    [Fact]
    public async Task Oversized_Image_Is_Rejected()
    {
        var (_, _, supportCase) = await _fixture.SeedCaseAsync();
        var externalId = $"img-big-{Guid.NewGuid():N}";
        var message = await _fixture.PersistInboundImageMessageAsync(supportCase.ConversationId, externalId);
        var png = TestImageBytes.CreatePng();

        var result = await _fixture.ProcessImageThroughPipelineAsync(
            supportCase,
            message,
            externalId,
            png,
            "image/png",
            fileLength: 2_000_000);

        Assert.Equal(MediaProcessOutcome.FileTooLarge, result.Outcome);
    }

    [Fact]
    public async Task Duplicate_ExternalMessageId_Does_Not_Duplicate_Storage()
    {
        var (_, _, supportCase) = await _fixture.SeedCaseAsync();
        var externalId = $"img-dup-{Guid.NewGuid():N}";
        var message = await _fixture.PersistInboundImageMessageAsync(supportCase.ConversationId, externalId);

        var first = await _fixture.ProcessImageThroughPipelineAsync(supportCase, message, externalId);
        var second = await _fixture.ProcessImageThroughPipelineAsync(supportCase, message, externalId);

        Assert.Equal(MediaProcessOutcome.Stored, first.Outcome);
        Assert.Equal(MediaProcessOutcome.AlreadyProcessed, second.Outcome);

        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.PanelLabelPhotos.CountAsync(p => p.TechnicalSupportCaseId == supportCase.Id));
    }

    [Fact]
    public async Task Multiple_Photos_Persist_With_Ordering()
    {
        var (_, _, supportCase) = await _fixture.SeedCaseAsync();
        for (var i = 1; i <= 3; i++)
        {
            var externalId = $"img-multi-{i}-{Guid.NewGuid():N}";
            var message = await _fixture.PersistInboundImageMessageAsync(supportCase.ConversationId, externalId);
            var result = await _fixture.ProcessImageThroughPipelineAsync(supportCase, message, externalId);
            Assert.Equal(MediaProcessOutcome.Stored, result.Outcome);
        }

        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var photos = await db.PanelLabelPhotos
            .Where(p => p.TechnicalSupportCaseId == supportCase.Id)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();
        Assert.Equal(3, photos.Count);
        Assert.Equal(new[] { 1, 2, 3 }, photos.Select(p => p.SortOrder));
    }

    [Fact]
    public async Task Different_Conversations_Do_Not_Collide()
    {
        var (_, _, caseA) = await _fixture.SeedCaseAsync("905551000101");
        var (_, _, caseB) = await _fixture.SeedCaseAsync("905551000102");

        var msgA = await _fixture.PersistInboundImageMessageAsync(caseA.ConversationId, $"img-a-{Guid.NewGuid():N}");
        var msgB = await _fixture.PersistInboundImageMessageAsync(caseB.ConversationId, $"img-b-{Guid.NewGuid():N}");

        var resultA = await _fixture.ProcessImageThroughPipelineAsync(caseA, msgA, msgA.ExternalMessageId!);
        var resultB = await _fixture.ProcessImageThroughPipelineAsync(caseB, msgB, msgB.ExternalMessageId!);

        Assert.NotEqual(resultA.StorageKey, resultB.StorageKey);
        Assert.Contains(caseA.Id.ToString("N"), resultA.StorageKey, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(caseB.Id.ToString("N"), resultB.StorageKey, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Download_Failure_Allows_Retry()
    {
        var (_, _, supportCase) = await _fixture.SeedCaseAsync();
        var externalId = $"img-retry-{Guid.NewGuid():N}";
        var message = await _fixture.PersistInboundImageMessageAsync(supportCase.ConversationId, externalId);

        var failed = await _fixture.ProcessImageThroughPipelineAsync(
            supportCase, message, externalId, configureDownload: false);
        Assert.Equal(MediaProcessOutcome.DownloadFailed, failed.Outcome);

        _fixture.WhatsApp.SetDownload(externalId, "image/png", TestImageBytes.CreatePng());
        var retry = await _fixture.ProcessImageThroughPipelineAsync(
            supportCase, message, externalId, configureDownload: false);
        Assert.Equal(MediaProcessOutcome.Stored, retry.Outcome);
    }

    [Fact]
    public async Task Handler_Duplicate_Event_Retries_Media_Without_Duplicate_Message()
    {
        var (_, customer, supportCase) = await _fixture.SeedCaseAsync();
        var externalId = $"img-handler-dup-{Guid.NewGuid():N}";
        _fixture.WhatsApp.SetDownloadFailure(externalId);

        var payload = new WhatsAppInboundEvent
        {
            EventType = "IncomingMessage",
            ExternalMessageId = externalId,
            RemotePhoneNumber = customer.PhoneNumber,
            FromMe = false,
            BotOriginated = false,
            MessageType = "Image",
            TextContent = "panel label",
            TextPresent = true,
            Timestamp = DateTimeOffset.UtcNow,
            Media = new WhatsAppMediaMetadata { MimeType = "image/png", Downloadable = true, FileLength = 1000 },
        };

        await _fixture.InvokeHandlerAsync(payload);
        await _fixture.InvokeHandlerAsync(payload);

        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Messages.CountAsync(m => m.ExternalMessageId == externalId));

        _fixture.WhatsApp.SetDownload(externalId, "image/png", TestImageBytes.CreatePng());
        await _fixture.InvokeHandlerAsync(payload);

        Assert.Equal(1, await db.PanelLabelPhotos.CountAsync(p => p.TechnicalSupportCaseId == supportCase.Id));
        var message = await db.Messages.SingleAsync(m => m.ExternalMessageId == externalId);
        Assert.NotNull(message.StorageKey);
    }

    [Fact]
    public async Task Captioned_Image_Persists_Caption_Without_Duplicate_Photo()
    {
        var (_, customer, supportCase) = await _fixture.SeedCaseAsync();
        var externalId = $"img-caption-{Guid.NewGuid():N}";
        _fixture.WhatsApp.SetDownload(externalId, "image/png", TestImageBytes.CreatePng());

        await _fixture.InvokeHandlerAsync(new WhatsAppInboundEvent
        {
            EventType = "IncomingMessage",
            ExternalMessageId = externalId,
            RemotePhoneNumber = customer.PhoneNumber,
            FromMe = false,
            MessageType = "Image",
            TextContent = "sol panel",
            TextPresent = true,
            Media = new WhatsAppMediaMetadata { MimeType = "image/png", Downloadable = true, FileLength = 500 },
        });

        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var message = await db.Messages.SingleAsync(m => m.ExternalMessageId == externalId);
        Assert.Equal("sol panel", message.Content);
        Assert.Equal(1, await db.PanelLabelPhotos.CountAsync(p => p.TechnicalSupportCaseId == supportCase.Id));
    }

    [Fact]
    public async Task Stored_Media_Survives_Service_Rebuild()
    {
        var (_, _, supportCase) = await _fixture.SeedCaseAsync();
        var externalId = $"img-persist-{Guid.NewGuid():N}";
        var message = await _fixture.PersistInboundImageMessageAsync(supportCase.ConversationId, externalId);
        var result = await _fixture.ProcessImageThroughPipelineAsync(supportCase, message, externalId);
        Assert.Equal(MediaProcessOutcome.Stored, result.Outcome);

        await using var scope = _fixture.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<IMediaStorage>();
        await using var stream = await storage.OpenReadAsync(result.StorageKey!);
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public async Task Panel_Photo_Satisfies_Workflow_Stage()
    {
        var (conversation, customer, _) = await _fixture.SeedCaseAsync();
        var externalId = $"img-stage-{Guid.NewGuid():N}";
        _fixture.WhatsApp.SetDownload(externalId, "image/png", TestImageBytes.CreatePng());

        await _fixture.InvokeHandlerAsync(new WhatsAppInboundEvent
        {
            EventType = "IncomingMessage",
            ExternalMessageId = externalId,
            RemotePhoneNumber = customer.PhoneNumber,
            FromMe = false,
            MessageType = "Image",
            Media = new WhatsAppMediaMetadata { MimeType = "image/png", Downloadable = true, FileLength = 500 },
        });

        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var updated = await db.Conversations.SingleAsync(c => c.Id == conversation.Id);
        Assert.Equal(ConversationStage.AskReceiverCount, updated.CurrentStage);
    }
}

public sealed class ConfigurableWhatsAppClient : IWhatsAppClient
{
    private readonly Dictionary<string, (string Mime, byte[] Content)> _downloads = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failures = new(StringComparer.Ordinal);

    public void SetDownload(string externalMessageId, string mimeType, byte[] content)
    {
        _failures.Remove(externalMessageId);
        _downloads[externalMessageId] = (mimeType, content);
    }

    public void SetDownloadFailure(string externalMessageId)
    {
        _failures.Add(externalMessageId);
        _downloads.Remove(externalMessageId);
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<WhatsAppConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(WhatsAppConnectionStatus.Connected);
    public Task<string?> SendTextMessageAsync(string toPhoneE164, string body, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>($"fake-{Guid.NewGuid():N}");
    public Task<string?> SendImageMessageAsync(string toPhoneE164, Stream image, string contentType, string? caption = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>($"fake-img-{Guid.NewGuid():N}");

    public Task SendComposingAsync(string toPhoneE164, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SendPausedPresenceAsync(string toPhoneE164, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<WhatsAppMediaDownloadResult> DownloadMediaAsync(string mediaId, CancellationToken cancellationToken = default)
    {
        if (_failures.Contains(mediaId))
        {
            throw new InvalidOperationException("download_failed");
        }

        if (!_downloads.TryGetValue(mediaId, out var item))
        {
            throw new InvalidOperationException("media_not_found");
        }

        return Task.FromResult(new WhatsAppMediaDownloadResult(mediaId, item.Mime, item.Content));
    }
}
