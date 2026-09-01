using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Infrastructure.Configuration;
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
        services.Configure<CorsOptions>(configuration.GetSection(CorsOptions.SectionName));
        services.Configure<WorkflowOptions>(configuration.GetSection(WorkflowOptions.SectionName));
        services.Configure<AiExtractionOptions>(configuration.GetSection(AiExtractionOptions.SectionName));
        var openAiSection = configuration.GetSection(OpenAiOptions.SectionName).Get<OpenAiOptions>() ?? new OpenAiOptions();
        services.PostConfigure<AiExtractionOptions>(opts => opts.ModelName = openAiSection.Model);

        services.AddSingleton<IBackgroundWorkQueue, InMemoryBackgroundWorkQueue>();
        RegisterAiServices(services, configuration);

        var connectionString = configuration.GetSection(DatabaseOptions.SectionName)["ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                });
            });

            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
            services.AddScoped<IConversationWorkflowService, ConversationWorkflowService>();
            services.AddScoped<IWorkflowOutboundDispatcher, WorkflowOutboundDispatcher>();
            services.AddScoped<AiExtractionValidator>();
            services.AddScoped<AiExtractionOrchestrator>();
            services.AddScoped<AiExtractionAuditWriter>();
        }

        var connectorEnabled = configuration.GetSection(WhatsAppConnectorOptions.SectionName).GetValue<bool>("Enabled");
        var connectorSecret = configuration.GetSection(WhatsAppConnectorOptions.SectionName)["SharedSecret"];
        if (connectorEnabled && !string.IsNullOrWhiteSpace(connectorSecret))
        {
            services.AddHttpClient<IWhatsAppClient, ConnectorWhatsAppClient>();
        }

        return services;
    }

    private static void RegisterAiServices(IServiceCollection services, IConfiguration configuration)
    {
        var openAiOptions = configuration.GetSection(OpenAiOptions.SectionName).Get<OpenAiOptions>() ?? new OpenAiOptions();
        if (!string.IsNullOrWhiteSpace(openAiOptions.ApiKey))
        {
            services.AddHttpClient<IAiService, OpenAiExtractionService>(client =>
            {
                client.BaseAddress = new Uri(openAiOptions.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(openAiOptions.TimeoutSeconds > 0 ? openAiOptions.TimeoutSeconds : 30);
            });
        }
        else
        {
            services.AddSingleton<IAiService, UnavailableAiService>();
        }
    }
}
