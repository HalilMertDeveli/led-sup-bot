using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LedSupBot.Application.Knowledge;
using LedSupBot.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace LedSupBot.Infrastructure.Knowledge;

public sealed class GeminiEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _http; private readonly GeminiOptions _gemini;
    public string Provider => "Gemini"; public string Model { get; } public int Dimensions { get; }
    public GeminiEmbeddingService(HttpClient http, IOptions<GeminiOptions> gemini, IOptions<EmbeddingOptions> options)
    { _http=http; _gemini=gemini.Value; Model=options.Value.Model; Dimensions=options.Value.Dimensions; }
    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_gemini.ApiKey)) throw new InvalidOperationException("Gemini API key is not configured for embeddings.");
        var result = new List<float[]>(texts.Count);
        foreach (var text in texts)
        {
            var uri = $"v1beta/models/{Uri.EscapeDataString(Model)}:embedContent";
            var body = new { model=$"models/{Model}", content=new { parts=new[] { new { text } } }, taskType="RETRIEVAL_DOCUMENT", outputDimensionality=Dimensions };
            for (var attempt=0;;attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body) };
                request.Headers.Add("x-goog-api-key", _gemini.ApiKey);
                using var response = await _http.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    using var json=JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
                    result.Add(json.RootElement.GetProperty("embedding").GetProperty("values").EnumerateArray().Select(x=>x.GetSingle()).ToArray()); break;
                }
                if (attempt < 3 && (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)) { await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2,attempt)),cancellationToken); continue; }
                throw new InvalidOperationException($"Gemini embedding request failed with HTTP {(int)response.StatusCode}.");
            }
        }
        return result;
    }
}
