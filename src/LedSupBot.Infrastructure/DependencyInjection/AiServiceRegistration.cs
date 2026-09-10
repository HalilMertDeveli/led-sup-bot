namespace LedSupBot.Infrastructure;

using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Application.Workflow.Web;
using LedSupBot.Infrastructure.Configuration;
using LedSupBot.Infrastructure.Gemini;
using LedSupBot.Infrastructure.OpenAi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

internal static class AiServiceRegistration
{
    public static void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));
        services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.SectionName));
        services.AddSingleton<GeminiProviderState>();
        services.AddSingleton<IGeminiProviderState>(provider => provider.GetRequiredService<GeminiProviderState>());

        if (AiProviderRegistration.IsGemini(configuration))
        {
            RegisterGemini(services, configuration);
            return;
        }

        if (AiProviderRegistration.IsOpenAi(configuration))
        {
            RegisterOpenAi(services, configuration);
            return;
        }

        RegisterUnavailable(services);
    }

    private static void RegisterGemini(IServiceCollection services, IConfiguration configuration)
    {
        var geminiOptions = configuration.GetSection(GeminiOptions.SectionName).Get<GeminiOptions>() ?? new GeminiOptions();
        services.PostConfigure<AiExtractionOptions>(opts => opts.ModelName = geminiOptions.Model);

        if (string.IsNullOrWhiteSpace(geminiOptions.ApiKey))
        {
            RegisterUnavailable(services);
            return;
        }

        services.AddHttpClient<GeminiStructuredClient>(client =>
        {
            client.BaseAddress = new Uri(geminiOptions.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(geminiOptions.TimeoutSeconds > 0 ? geminiOptions.TimeoutSeconds : 30);
        });

        services.AddScoped<IAiService, GeminiExtractionService>();
        services.AddScoped<IAiConversationPlanner, GeminiConversationPlannerService>();
        services.AddScoped<IWebGroundingService, GeminiWebGroundingService>();
    }

    private static void RegisterOpenAi(IServiceCollection services, IConfiguration configuration)
    {
        var openAiOptions = configuration.GetSection(OpenAiOptions.SectionName).Get<OpenAiOptions>() ?? new OpenAiOptions();
        services.PostConfigure<AiExtractionOptions>(opts => opts.ModelName = openAiOptions.Model);

        if (string.IsNullOrWhiteSpace(openAiOptions.ApiKey))
        {
            RegisterUnavailable(services);
            return;
        }

        services.AddHttpClient<IAiService, OpenAiExtractionService>(client =>
        {
            client.BaseAddress = new Uri(openAiOptions.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(openAiOptions.TimeoutSeconds > 0 ? openAiOptions.TimeoutSeconds : 30);
        });

        services.AddHttpClient<IAiConversationPlanner, OpenAiConversationPlannerService>(client =>
        {
            client.BaseAddress = new Uri(openAiOptions.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(openAiOptions.TimeoutSeconds > 0 ? openAiOptions.TimeoutSeconds : 30);
        });
        services.AddSingleton<IWebGroundingService, UnavailableWebGroundingService>();
    }

    private static void RegisterUnavailable(IServiceCollection services)
    {
        services.AddSingleton<IAiService, UnavailableAiService>();
        services.AddSingleton<IAiConversationPlanner, UnavailableAiConversationPlanner>();
        services.AddSingleton<IWebGroundingService, UnavailableWebGroundingService>();
    }
}
