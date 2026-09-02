using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using HealthChecks.UI.Client;
using LedSupBot.Application;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Admin;
using LedSupBot.Application.Takeover;
using LedSupBot.Application.WhatsApp;
using LedSupBot.Application.Workflow;
using LedSupBot.Domain.Enums;
using LedSupBot.Infrastructure;
using LedSupBot.Infrastructure.Configuration;
using LedSupBot.Infrastructure.Gemini;
using LedSupBot.Api.Admin;
using LedSupBot.Api.Middleware;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddFlatEnvironmentVariableBridge();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddApplication();
builder.Services.Configure<LedSupBot.Application.WhatsApp.WhatsAppTestModeOptions>(
    builder.Configuration.GetSection(LedSupBot.Application.WhatsApp.WhatsAppTestModeOptions.SectionName));
builder.Services.Configure<LedSupBot.Application.WhatsApp.WhatsAppAutomationOptions>(
    builder.Configuration.GetSection(LedSupBot.Application.WhatsApp.WhatsAppAutomationOptions.SectionName));
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "LED Support Bot API",
        Version = "v1",
        Description = "Phase 4.5 personal WhatsApp connector (unofficial Baileys). Not Meta-approved. No AI/workflow automation."
    });
});

var databaseOptions = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
    ?? new DatabaseOptions();

var healthChecks = builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("API process is running."));

if (!string.IsNullOrWhiteSpace(databaseOptions.ConnectionString))
{
    healthChecks.AddNpgSql(
        databaseOptions.ConnectionString,
        name: "postgresql",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["db", "postgresql"]);
}

builder.Services.AddCors();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IAdminEventNotifier, SignalRAdminEventNotifier>();

var app = builder.Build();

var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
var workflowOptions = app.Services.GetRequiredService<IOptions<WorkflowOptions>>().Value;
var connectorOptionsAtStartup = app.Services.GetRequiredService<IOptions<WhatsAppConnectorOptions>>().Value;
startupLogger.LogInformation(
    "Startup pipeline: DatabaseConfigured={DatabaseConfigured} WorkflowAutoReplyEnabled={AutoReplyEnabled} ConnectorEnabled={ConnectorEnabled} ConnectorSecretConfigured={ConnectorSecretConfigured} WhatsAppTestMode={TestModeEnabled}",
    !string.IsNullOrWhiteSpace(databaseOptions.ConnectionString),
    workflowOptions.AutoReplyEnabled,
    connectorOptionsAtStartup.Enabled,
    !string.IsNullOrWhiteSpace(connectorOptionsAtStartup.SharedSecret),
    app.Services.GetRequiredService<IOptions<WhatsAppTestModeOptions>>().Value.Enabled);

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var corsOptions = app.Services.GetRequiredService<IOptions<LedSupBot.Infrastructure.Configuration.CorsOptions>>().Value;
app.UseCors(policy =>
{
    if (corsOptions.AllowedOrigins.Length == 0)
    {
        return;
    }

    policy.WithOrigins(corsOptions.AllowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
});

var aiOptions = app.Services.GetRequiredService<IOptions<AiOptions>>().Value;
var geminiOptions = app.Services.GetRequiredService<IOptions<GeminiOptions>>().Value;
var aiStatus = string.Equals(aiOptions.Provider, "Gemini", StringComparison.OrdinalIgnoreCase)
    ? (string.IsNullOrWhiteSpace(geminiOptions.ApiKey) ? "gemini_not_configured" : $"gemini:{geminiOptions.Model}")
    : string.Equals(aiOptions.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase)
        ? "openai_explicit"
        : "unavailable";

app.MapGet("/", () => Results.Ok(new
{
    service = "LedSupBot.Api",
    phase = "9",
    status = "admin_dashboard",
    whatsAppIntegration = "baileys_connector_unofficial",
    metaApproved = false,
    aiProvider = aiOptions.Provider,
    aiIntegration = aiStatus,
    databaseSchema = "ef_core_postgresql",
    autoMigrateOnStartup = false,
    autoCustomerReplies = false
}));

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapGet("/api/foundation/status", async (
    IWhatsAppConnectionStateStore state,
    IServiceProvider services,
    IOptions<WhatsAppTestModeOptions> testMode,
    CancellationToken cancellationToken) =>
{
    var whatsAppConnection = await WhatsAppLiveStatusResolver.GetCurrentAsync(services, state, cancellationToken);
    var testModeOptions = testMode.Value;
    return Results.Ok(new
    {
        architecture = "hybrid-workflow-plus-ai-boundaries",
        botOwnsStateMachine = false,
        backendOwnsWorkflow = true,
        humanTakeoverSupportedByDesign = true,
        persistence = "postgresql_ef_core",
        whatsAppConnection = whatsAppConnection.ToString(),
        whatsAppTestMode = new
        {
            enabled = testModeOptions.Enabled,
            identityConfigured = PhoneNumberNormalizer.TryNormalizeToCanonicalDigits(testModeOptions.AllowedPhoneNumber) is not null
        }
    });
});

app.MapPost("/internal/whatsapp/events", async (
    HttpRequest request,
    IOptions<WhatsAppConnectorOptions> connectorOptions,
    IWhatsAppInboundEventHandler handler,
    CancellationToken cancellationToken) =>
{
    if (!IsAuthorized(request, connectorOptions.Value.SharedSecret))
    {
        return Results.Unauthorized();
    }

    var inbound = await request.ReadFromJsonAsync<WhatsAppInboundEvent>(cancellationToken: cancellationToken);
    if (inbound is null || string.IsNullOrWhiteSpace(inbound.EventType))
    {
        return Results.BadRequest(new { error = "invalid_event" });
    }

    await handler.HandleAsync(inbound, cancellationToken);
    return Results.Accepted();
});

app.MapGet("/internal/whatsapp/status", async (
    HttpRequest request,
    IOptions<WhatsAppConnectorOptions> connectorOptions,
    IWhatsAppConnectionStateStore stateStore,
    IServiceProvider services,
    CancellationToken cancellationToken) =>
{
    if (!IsAuthorized(request, connectorOptions.Value.SharedSecret))
    {
        return Results.Unauthorized();
    }

    var client = services.GetService<IWhatsAppClient>();
    if (client is not null)
    {
        var live = await client.GetConnectionStatusAsync(cancellationToken);
        return Results.Ok(new { status = live.ToString(), source = "connector" });
    }

    return Results.Ok(new { status = stateStore.Current.ToString(), source = "memory" });
});

app.MapPost("/internal/whatsapp/send-text", async (
    HttpRequest request,
    IOptions<WhatsAppConnectorOptions> connectorOptions,
    IServiceProvider services,
    IWhatsAppOutboundRecorder recorder,
    CancellationToken cancellationToken) =>
{
    if (!IsAuthorized(request, connectorOptions.Value.SharedSecret))
    {
        return Results.Unauthorized();
    }

    var client = services.GetService<IWhatsAppClient>();
    if (client is null)
    {
        return Results.Problem("WhatsApp connector client is not enabled. Set WhatsAppConnector:Enabled=true and SharedSecret.");
    }

    var body = await request.ReadFromJsonAsync<SendTextRequest>(cancellationToken: cancellationToken);
    if (body is null || string.IsNullOrWhiteSpace(body.To) || string.IsNullOrWhiteSpace(body.Text))
    {
        return Results.BadRequest(new { error = "to_and_text_required" });
    }

    string? externalId;
    try
    {
        // Controlled test endpoint only — no automatic customer replies.
        externalId = await client.SendTextMessageAsync(body.To, body.Text, cancellationToken);
    }
    catch (Exception)
    {
        // Connector offline / send failure must not crash the API process.
        return Results.Problem(
            detail: "WhatsApp connector unavailable or send failed.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    // Persist immediately so fromMe echoes correlate as Bot even if connector memory is lost.
    await recorder.RecordBotTextAsync(null, body.To, body.Text, externalId, cancellationToken);

    return Results.Ok(new { ok = true, externalMessageId = externalId });
});

app.MapPost("/internal/conversations/{conversationId:guid}/takeover", async (
    Guid conversationId,
    HttpRequest request,
    IOptions<WhatsAppConnectorOptions> connectorOptions,
    IConversationTakeoverService takeover,
    CancellationToken cancellationToken) =>
{
    if (!IsAuthorized(request, connectorOptions.Value.SharedSecret))
    {
        return Results.Unauthorized();
    }

    var result = await takeover.TakeOverAsync(conversationId, TakeoverChangeSource.AdminTakeover, cancellationToken);
    return result.Found
        ? Results.Ok(new { ok = true, changed = result.Changed, botMode = result.CurrentMode.ToString(), stage = result.CurrentStage.ToString() })
        : Results.NotFound(new { error = "conversation_not_found" });
});

app.MapPost("/internal/conversations/{conversationId:guid}/pause", async (
    Guid conversationId,
    HttpRequest request,
    IOptions<WhatsAppConnectorOptions> connectorOptions,
    IConversationTakeoverService takeover,
    CancellationToken cancellationToken) =>
{
    if (!IsAuthorized(request, connectorOptions.Value.SharedSecret))
    {
        return Results.Unauthorized();
    }

    var result = await takeover.PauseAsync(conversationId, cancellationToken);
    return result.Found
        ? Results.Ok(new { ok = true, changed = result.Changed, botMode = result.CurrentMode.ToString() })
        : Results.NotFound(new { error = "conversation_not_found" });
});

app.MapPost("/internal/conversations/{conversationId:guid}/resume-bot", async (
    Guid conversationId,
    HttpRequest request,
    IOptions<WhatsAppConnectorOptions> connectorOptions,
    IConversationTakeoverService takeover,
    CancellationToken cancellationToken) =>
{
    if (!IsAuthorized(request, connectorOptions.Value.SharedSecret))
    {
        return Results.Unauthorized();
    }

    var result = await takeover.ResumeBotAsync(conversationId, cancellationToken);
    return result.Found
        ? Results.Ok(new { ok = true, changed = result.Changed, botMode = result.CurrentMode.ToString(), stage = result.CurrentStage.ToString() })
        : Results.NotFound(new { error = "conversation_not_found" });
});

app.MapAdminEndpoints();
app.MapHub<AdminHub>("/hubs/admin");

app.Run();

static bool IsAuthorized(HttpRequest request, string? sharedSecret)
{
    if (string.IsNullOrWhiteSpace(sharedSecret))
    {
        return false;
    }

    if (!request.Headers.TryGetValue("X-Connector-Secret", out var provided))
    {
        return false;
    }

    var a = Encoding.UTF8.GetBytes(provided.ToString());
    var b = Encoding.UTF8.GetBytes(sharedSecret);
    return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
}

internal sealed record SendTextRequest(string To, string Text);

public partial class Program;
