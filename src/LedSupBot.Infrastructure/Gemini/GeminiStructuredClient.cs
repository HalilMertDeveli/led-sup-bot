using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LedSupBot.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LedSupBot.Infrastructure.Gemini;

public sealed class GeminiStructuredClient
{
    private readonly HttpClient _http;
    private readonly GeminiOptions _options;
    private readonly ILogger _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public GeminiStructuredClient(HttpClient http, IOptions<GeminiOptions> options, ILogger<GeminiStructuredClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> TryGenerateJsonAsync(
        string operation,
        string systemPrompt,
        string userPrompt,
        object responseSchema,
        double temperature,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning("Gemini API key is not configured for {Operation}", operation);
            return null;
        }

        var model = _options.Model.Trim();
        var url =
            $"{_options.BaseUrl.TrimEnd('/')}/v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(_options.ApiKey)}";

        var payload = new GenerateContentRequest
        {
            SystemInstruction = new ContentParts(systemPrompt),
            Contents = [new ContentEntry("user", userPrompt)],
            GenerationConfig = new GenerationConfig
            {
                Temperature = temperature,
                ResponseMimeType = "application/json",
                ResponseSchema = responseSchema,
            },
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(url, payload, JsonOptions, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Gemini request timed out for {Operation}; using deterministic fallback", operation);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Gemini request failed for {Operation}; using deterministic fallback", operation);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Gemini request failed for {Operation}: status={StatusCode} body={Body}; using deterministic fallback",
                operation,
                (int)response.StatusCode,
                errorBody);
            return null;
        }

        GenerateContentResponse? body;
        try
        {
            body = await response.Content.ReadFromJsonAsync<GenerateContentResponse>(JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Gemini response parse failed for {Operation}", operation);
            return null;
        }

        var text = body?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private sealed class GenerateContentRequest
    {
        public ContentParts? SystemInstruction { get; set; }
        public List<ContentEntry> Contents { get; set; } = [];
        public GenerationConfig? GenerationConfig { get; set; }
    }

    private sealed class ContentParts
    {
        public ContentParts(string text) => Parts = [new TextPart(text)];

        public List<TextPart> Parts { get; set; } = [];
    }

    private sealed class ContentEntry
    {
        public ContentEntry(string role, string text)
        {
            Role = role;
            Parts = [new TextPart(text)];
        }

        public string Role { get; set; } = "";
        public List<TextPart> Parts { get; set; } = [];
    }

    private sealed record TextPart(string Text);

    private sealed class GenerationConfig
    {
        public double Temperature { get; set; }
        public string ResponseMimeType { get; set; } = "application/json";
        public object? ResponseSchema { get; set; }
    }

    private sealed class GenerateContentResponse
    {
        public List<Candidate>? Candidates { get; set; }
    }

    private sealed class Candidate
    {
        public CandidateContent? Content { get; set; }
    }

    private sealed class CandidateContent
    {
        public List<TextPart>? Parts { get; set; }
    }
}
