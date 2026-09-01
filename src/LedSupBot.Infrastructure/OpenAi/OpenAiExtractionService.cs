using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LedSupBot.Application.Abstractions;
using LedSupBot.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LedSupBot.Infrastructure.OpenAi;

public sealed class OpenAiExtractionService : IAiService
{
    public const string PromptVersion = "phase6-v1";
    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiExtractionService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public OpenAiExtractionService(
        HttpClient http,
        IOptions<OpenAiOptions> options,
        ILogger<OpenAiExtractionService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiStructuredExtraction> ExtractAsync(
        AiExtractionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("OpenAI API key is not configured.");
        }

        var payload = new ChatCompletionRequest
        {
            Model = _options.Model,
            Temperature = 0,
            Messages =
            [
                new ChatMessage("system", BuildSystemPrompt()),
                new ChatMessage("user", BuildUserPrompt(request)),
            ],
            ResponseFormat = new ResponseFormatWrapper
            {
                Type = "json_schema",
                JsonSchema = new JsonSchemaWrapper
                {
                    Name = "technical_extraction",
                    Strict = true,
                    Schema = ExtractionJsonSchema.Document,
                },
            },
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await _http.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("OpenAI request failed with status {StatusCode}", (int)response.StatusCode);
            throw new InvalidOperationException($"OpenAI request failed: {(int)response.StatusCode}");
        }

        var completion = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(JsonOptions, cancellationToken);
        var content = completion?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("OpenAI returned empty structured content.");
        }

        OpenAiExtractionPayload? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<OpenAiExtractionPayload>(content, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "OpenAI returned malformed structured JSON");
            throw;
        }

        if (parsed is null)
        {
            throw new InvalidOperationException("OpenAI structured payload could not be parsed.");
        }

        return MapToDomain(parsed);
    }

    private static string BuildSystemPrompt() =>
        """
        You extract LED technical support information from Turkish WhatsApp customer messages.
        Rules:
        - Database storage is ALWAYS centimeters (widthCm/heightCm). Never output meters in cm fields.
        - When customer explicitly says metre/meter/m, populate widthMeters/heightMeters (not cm fields).
        - When customer explicitly says cm, populate widthCm/heightCm.
        - For "5x3" or "500x300" without units, set unitAmbiguous=true and leave cm/meter values null.
        - Do not guess units when context is insufficient.
        - Recognize receiver/reciever/reciver, yeşil kart, kart, adet, tane.
        - Config: evet, bağladım, tamam, olur = confirmed; hayır, olmadı = rejection.
        - AnyDesk: extract digit groups only when clearly an AnyDesk address.
        - NEVER claim a photo/image was received — only text extraction.
        - Return confidence 0-1 per section. Use low confidence when uncertain.
        - List ambiguities in Turkish or English short phrases.
        """;

    private static string BuildUserPrompt(AiExtractionRequest request)
    {
        var known = request.KnownSlots.Count == 0
            ? "none"
            : string.Join(", ", request.KnownSlots.Select(kv => $"{kv.Key}={kv.Value ?? "missing"}"));

        return $"""
            Language: {request.LanguageHint ?? "tr"}
            Already known slots: {known}
            Customer message:
            {request.MessageText}
            """;
    }

    private static AiStructuredExtraction MapToDomain(OpenAiExtractionPayload payload) =>
        new(
            MapIntent(payload.Intent),
            payload.Screen is null ? null : new ScreenSlotCandidate(
                payload.Screen.WidthCm,
                payload.Screen.HeightCm,
                payload.Screen.WidthMeters,
                payload.Screen.HeightMeters,
                payload.Screen.Confidence,
                payload.Screen.UnitAmbiguous),
            payload.Receiver is null ? null : new ReceiverSlotCandidate(payload.Receiver.Count, payload.Receiver.Confidence),
            payload.Config is null ? null : new ConfigSlotCandidate(payload.Config.Connected, payload.Config.Confirmed, payload.Config.Confidence),
            payload.AnyDesk is null ? null : new AnyDeskSlotCandidate(payload.AnyDesk.Address, payload.AnyDesk.Confidence),
            payload.Ambiguities ?? []);

    private static CustomerMessageIntent MapIntent(string? intent) =>
        intent?.ToLowerInvariant() switch
        {
            "greeting" => CustomerMessageIntent.Greeting,
            "technical_support" => CustomerMessageIntent.TechnicalSupport,
            "confirmation" => CustomerMessageIntent.Confirmation,
            "rejection" => CustomerMessageIntent.Rejection,
            _ => CustomerMessageIntent.Unknown,
        };

    private sealed class ChatCompletionRequest
    {
        public string Model { get; set; } = string.Empty;
        public double Temperature { get; set; }
        public List<ChatMessage> Messages { get; set; } = [];
        public ResponseFormatWrapper? ResponseFormat { get; set; }
    }

    private sealed record ChatMessage(string Role, string Content);

    private sealed class ResponseFormatWrapper
    {
        public string Type { get; set; } = string.Empty;
        public JsonSchemaWrapper JsonSchema { get; set; } = null!;
    }

    private sealed class JsonSchemaWrapper
    {
        public string Name { get; set; } = string.Empty;
        public bool Strict { get; set; }
        public object Schema { get; set; } = null!;
    }

    private sealed class ChatCompletionResponse
    {
        public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        public ChoiceMessage? Message { get; set; }
    }

    private sealed class ChoiceMessage
    {
        public string? Content { get; set; }
    }

    private sealed class OpenAiExtractionPayload
    {
        public string? Intent { get; set; }
        public ScreenPayload? Screen { get; set; }
        public ReceiverPayload? Receiver { get; set; }
        public ConfigPayload? Config { get; set; }
        public AnyDeskPayload? AnyDesk { get; set; }
        public List<string>? Ambiguities { get; set; }
    }

    private sealed class ScreenPayload
    {
        public decimal? WidthCm { get; set; }
        public decimal? HeightCm { get; set; }
        public decimal? WidthMeters { get; set; }
        public decimal? HeightMeters { get; set; }
        public double Confidence { get; set; }
        public bool UnitAmbiguous { get; set; }
    }

    private sealed class ReceiverPayload
    {
        public int? Count { get; set; }
        public double Confidence { get; set; }
    }

    private sealed class ConfigPayload
    {
        public bool? Connected { get; set; }
        public bool? Confirmed { get; set; }
        public double Confidence { get; set; }
    }

    private sealed class AnyDeskPayload
    {
        public string? Address { get; set; }
        public double Confidence { get; set; }
    }
}
