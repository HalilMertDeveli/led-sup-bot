using LedSupBot.Application.Workflow.Web;

namespace LedSupBot.Infrastructure.Gemini;

public sealed class GeminiWebGroundingService : IWebGroundingService
{
    private readonly GeminiStructuredClient _client;
    public GeminiWebGroundingService(GeminiStructuredClient client) => _client = client;

    public Task<WebGroundingResult> SearchAsync(string publicQuestion, CancellationToken cancellationToken = default) =>
        _client.TryGenerateWebGroundedAsync(publicQuestion, cancellationToken);
}
