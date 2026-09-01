using LedSupBot.Application.Abstractions;

namespace LedSupBot.Infrastructure.OpenAi;

/// <summary>Fallback when OpenAI is not configured. Never throws — workflow uses deterministic parsers.</summary>
public sealed class UnavailableAiService : IAiService
{
    public Task<AiStructuredExtraction> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AiStructuredExtraction(
            CustomerMessageIntent.Unknown,
            null,
            null,
            null,
            null,
            []));
}
