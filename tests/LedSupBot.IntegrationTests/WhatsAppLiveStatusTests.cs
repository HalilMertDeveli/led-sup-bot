using System.Net;
using System.Net.Http.Json;
using LedSupBot.Application.Abstractions;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace LedSupBot.IntegrationTests;

public sealed class WhatsAppLiveStatusFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("led_sup_bot_status")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public RecordingWhatsAppClient WhatsApp { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using (var db = CreateDb())
        {
            await db.Database.MigrateAsync();
        }

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:ConnectionString", _postgres.GetConnectionString());
            builder.UseSetting("WhatsAppConnector:Enabled", "true");
            builder.UseSetting("WhatsAppConnector:SharedSecret", "test-secret");
            builder.UseSetting("WhatsAppConnector:BaseUrl", "http://127.0.0.1:9");
            builder.UseSetting("WhatsAppTestMode:Enabled", "true");
            builder.UseSetting("WhatsAppTestMode:AllowedPhoneNumber", "05425192119");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IWhatsAppClient>();
                services.AddSingleton(WhatsApp);
                services.AddSingleton<IWhatsAppClient>(sp => sp.GetRequiredService<RecordingWhatsAppClient>());
            });
        });
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new ApplicationDbContext(options);
    }
}

[CollectionDefinition("whatsapp-live-status")]
public sealed class WhatsAppLiveStatusCollection : ICollectionFixture<WhatsAppLiveStatusFixture>;

[Collection("whatsapp-live-status")]
public sealed class WhatsAppLiveStatusTests
{
    private readonly WhatsAppLiveStatusFixture _fixture;

    public WhatsAppLiveStatusTests(WhatsAppLiveStatusFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Foundation_status_reports_connected_from_live_client()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.GetAsync("/api/foundation/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<FoundationStatusPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Connected", payload!.WhatsAppConnection);
        Assert.True(payload.WhatsAppTestMode?.Enabled);
        Assert.True(payload.WhatsAppTestMode?.IdentityConfigured);
    }

    private sealed class FoundationStatusPayload
    {
        public string? WhatsAppConnection { get; set; }
        public FoundationTestModePayload? WhatsAppTestMode { get; set; }
    }

    private sealed class FoundationTestModePayload
    {
        public bool Enabled { get; set; }
        public bool IdentityConfigured { get; set; }
    }
}
