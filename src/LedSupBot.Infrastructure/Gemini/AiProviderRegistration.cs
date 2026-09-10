using LedSupBot.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace LedSupBot.Infrastructure.Gemini;

internal static class AiProviderRegistration
{
    public const string Gemini = "Gemini";
    public const string OpenAI = "OpenAI";

    public static string ResolveProvider(IConfiguration configuration)
    {
        var configured = configuration.GetSection(AiOptions.SectionName)["Provider"];
        return string.IsNullOrWhiteSpace(configured) ? Gemini : configured.Trim();
    }

    public static bool IsGemini(IConfiguration configuration) =>
        string.Equals(ResolveProvider(configuration), Gemini, StringComparison.OrdinalIgnoreCase);

    public static bool IsOpenAi(IConfiguration configuration) =>
        string.Equals(ResolveProvider(configuration), OpenAI, StringComparison.OrdinalIgnoreCase);
}
