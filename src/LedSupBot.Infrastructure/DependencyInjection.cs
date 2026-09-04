using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Admin;
using LedSupBot.Application.Media;
using LedSupBot.Application.Knowledge;
using LedSupBot.Application.Takeover;
using LedSupBot.Application.Training;
using LedSupBot.Application.WhatsApp;
using LedSupBot.Application.Workflow;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Application.Workflow.Diagnostics;
using LedSupBot.Infrastructure.Configuration;
using LedSupBot.Infrastructure.Gemini;
using LedSupBot.Infrastructure.Media;
using LedSupBot.Infrastructure.Knowledge;
using LedSupBot.Infrastructure.OpenAi;
using LedSupBot.Infrastructure.Persistence;
using LedSupBot.Infrastructure.Queue;
using LedSupBot.Infrastructure.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LedSupBot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.SectionName));
        services.Configure<WhatsAppOptions>(configuration.GetSection(WhatsAppOptions.SectionName));
        services.Configure<WhatsAppConnectorOptions>(configuration.GetSection(WhatsAppConnectorOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<MediaStorageOptions>(configuration.GetSection(MediaStorageOptions.SectionName));
        services.Configure<CorsOptions>(configuration.GetSection(CorsOptions.SectionName));
        services.Configure<WorkflowOptions>(configuration.GetSection(WorkflowOptions.SectionName));
        services.Configure<AiExtractionOptions>(configuration.GetSection(AiExtractionOptions.SectionName));
        services.Configure<AdminOptions>(configuration.GetSection(AdminOptions.SectionName));
        services.Configure<WhatsAppTestModeOptions>(configuration.GetSection(WhatsAppTestModeOptions.SectionName));
        services.Configure<WhatsAppAutomationOptions>(configuration.GetSection(WhatsAppAutomationOptions.SectionName));
        services.Configure<KnowledgeOptions>(configuration.GetSection(KnowledgeOptions.SectionName));
        services.Configure<TrainingCollectionOptions>(configuration.GetSection(TrainingCollectionOptions.SectionName));
        services.Configure<EmbeddingOptions>(configuration.GetSection(EmbeddingOptions.SectionName));
        services.AddScoped<IPdfKnowledgeExtractor, PdfKnowledgeExtractor>();
        services.AddHttpClient<IDocumentFallbackExtractor, GeminiDocumentFallbackExtractor>((sp, client) =>
        {
            var value = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GeminiOptions>>().Value;
            client.BaseAddress = new Uri(value.BaseUrl); client.Timeout = TimeSpan.FromMinutes(4);
        });
        services.AddHttpClient<IEmbeddingService, GeminiEmbeddingService>((sp, client) =>
        {
            var value = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GeminiOptions>>().Value;
            client.BaseAddress = new Uri(value.BaseUrl); client.Timeout = TimeSpan.FromSeconds(value.TimeoutSeconds);
        });
        services.AddHttpClient<ITechnicalImageAnalyzer, GeminiTechnicalImageAnalyzer>((sp, client) =>
        {
            var value = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GeminiOptions>>().Value;
            client.BaseAddress = new Uri(value.BaseUrl); client.Timeout = TimeSpan.FromSeconds(Math.Clamp(value.TimeoutSeconds, 5, 60));
        });

        services.AddSingleton<IBackgroundWorkQueue, InMemoryBackgroundWorkQueue>();
        AiServiceRegistration.Register(services, configuration);

        var connectionString = configuration.GetSection(DatabaseOptions.SectionName)["ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                    npgsql.UseVector();
                });
            });

            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
            services.AddScoped<KnowledgeImporter>();
            services.AddScoped<ITechnicalKnowledgeRetriever, TechnicalKnowledgeRetriever>();
            services.AddScoped<IConversationTakeoverService, ConversationTakeoverService>();
            services.AddScoped<IConversationWorkflowService, ConversationWorkflowService>();
            services.AddScoped<IWorkflowOutboundDispatcher, WorkflowOutboundDispatcher>();
            services.AddSingleton<IConversationReplyCoordinator, ConversationReplyCoordinator>();
            services.AddScoped<IConversationContextBuilder, ConversationContextBuilder>();
            services.AddScoped<HybridResponseComposer>();
            services.AddScoped<AiResponsePlanner>();
            services.AddScoped<AiExtractionValidator>();
            services.AddScoped<AiExtractionOrchestrator>();
            services.AddScoped<AiExtractionAuditWriter>();
            services.AddSingleton<IMediaStorage, LocalMediaStorage>();
            services.AddSingleton<IMediaImageValidator, MediaImageValidator>();
            services.AddScoped<IInboundMediaPipeline, InboundMediaPipeline>();
            services.AddScoped<TechnicalImageAnalysisService>();
            services.AddScoped<DiagnosticStateService>();
            services.AddScoped<IAdminConversationService, AdminConversationService>();
            services.AddScoped<IAdminTechnicianOutboundService, AdminTechnicianOutboundService>();
            services.AddScoped<IAiTrainingService, AiTrainingService>();
        }

        var connectorEnabled = configuration.GetSection(WhatsAppConnectorOptions.SectionName).GetValue<bool>("Enabled");
        var connectorSecret = configuration.GetSection(WhatsAppConnectorOptions.SectionName)["SharedSecret"];
        if (connectorEnabled && !string.IsNullOrWhiteSpace(connectorSecret))
        {
            services.AddHttpClient<IWhatsAppClient, ConnectorWhatsAppClient>();
            services.AddHttpClient<IWhatsAppSelfIdentitySync, WhatsAppSelfIdentitySync>((sp, client) =>
            {
                var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WhatsAppConnectorOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.Remove("X-Connector-Secret");
                client.DefaultRequestHeaders.Add("X-Connector-Secret", options.SharedSecret);
            });
        }
        else
        {
            services.AddSingleton<IWhatsAppClient, UnavailableWhatsAppClient>();
        }

        return services;
    }
}
