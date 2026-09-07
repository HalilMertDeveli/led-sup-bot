using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow.Parsing;
using LedSupBot.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LedSupBot.Infrastructure.OpenAi;

public sealed class OpenAiExtractionService : IAiService
{
    public const string PromptVersion = "phase5-v1";

    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiExtractionService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
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

    public async Task<AiStructuredExtraction> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning("OpenAI API key is not configured; AI extraction unavailable");
            return new AiStructuredExtraction(
                CustomerMessageIntent.Unknown,
                Screen: null,
                Receiver: null,
                Config: null,
                AnyDesk: null,
                Ambiguities: ["openai_unconfigured"]);
        }

        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(request);

        var payload = new ChatCompletionRequest
        {
            Model = _options.Model,
            Temperature = 0.1,
            Messages =
            [
                new ChatMessage("system", systemPrompt),
                new ChatMessage("user", userPrompt),
            ],
            ResponseFormat = new ResponseFormatWrapper
            {
                Type = "json_schema",
                JsonSchema = new JsonSchemaWrapper
                {
                    Name = "led_support_slot_extraction",
                    Strict = true,
                    Schema = ExtractionJsonSchema.Document,
                },
            },
        };

        HttpResponseMessage response;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
            req.Content = JsonContent.Create(payload, options: JsonOptions);
            response = await _http.SendAsync(req, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("OpenAI extraction request timed out; falling back to deterministic parsers");
            return new AiStructuredExtraction(
                CustomerMessageIntent.Unknown,
                Screen: null,
                Receiver: null,
                Config: null,
                AnyDesk: null,
                Ambiguities: ["openai_timeout"]);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "OpenAI extraction HTTP request failed");
            return new AiStructuredExtraction(
                CustomerMessageIntent.Unknown,
                Screen: null,
                Receiver: null,
                Config: null,
                AnyDesk: null,
                Ambiguities: ["openai_http_error"]);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("OpenAI extraction returned status {StatusCode}", (int)response.StatusCode);
            return new AiStructuredExtraction(
                CustomerMessageIntent.Unknown,
                Screen: null,
                Receiver: null,
                Config: null,
                AnyDesk: null,
                Ambiguities: [$"openai_status_{(int)response.StatusCode}"]);
        }

        ChatCompletionResponse? completion;
        try
        {
            completion = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "OpenAI extraction response envelope parse failed");
            return new AiStructuredExtraction(
                CustomerMessageIntent.Unknown,
                Screen: null,
                Receiver: null,
                Config: null,
                AnyDesk: null,
                Ambiguities: ["openai_envelope_invalid"]);
        }

        var rawContent = completion?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            _logger.LogWarning("OpenAI extraction choice was empty");
            return new AiStructuredExtraction(
                CustomerMessageIntent.Unknown,
                Screen: null,
                Receiver: null,
                Config: null,
                AnyDesk: null,
                Ambiguities: ["openai_empty_content"]);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<OpenAiExtractionPayload>(rawContent, JsonOptions);
            if (parsed is null)
            {
                return new AiStructuredExtraction(
                    CustomerMessageIntent.Unknown,
                    Screen: null,
                    Receiver: null,
                    Config: null,
                    AnyDesk: null,
                    Ambiguities: ["openai_deserialization_null"]);
            }

            return MapToDomain(parsed);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "OpenAI extraction content JSON deserialization failed");
            return new AiStructuredExtraction(
                CustomerMessageIntent.Unknown,
                Screen: null,
                Receiver: null,
                Config: null,
                AnyDesk: null,
                Ambiguities: ["openai_json_invalid"]);
        }
    }

    private static string BuildSystemPrompt() =>
        """
        You are a highly precise technical assistant for a Turkish LED screen technical support company.
        Your task is to extract structured slots from customer WhatsApp messages.
        Slots to extract:
        - Screen dimensions: width and height in centimeters (or meters if specified).
        - Receiver information: count (integer) and model (e.g. E120, E80, 5A75E, E320 Pro).
        - Player type: Playbox or Processor.
        - Processor HDMI: connected (boolean) and clarification needed (boolean).
        - Config cable connection: connected and confirmed.
        - AnyDesk remote access: 9-10 digit address.
        - Customer intent: greeting, technical_support, confirmation, rejection, ask_clarification, ask_technical_question, correction, acknowledgement.
        Output ONLY valid JSON matching the schema.
        """;

    private static string BuildUserPrompt(AiExtractionRequest request)
    {
        var known = string.Join("; ", request.KnownSlots.Select(kv => $"{kv.Key}={kv.Value ?? "unknown"}"));
        return $"""
            Known slots: {known}
            Message:
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
            payload.Receiver is null ? null : new ReceiverSlotCandidate(
                payload.Receiver.Count,
                payload.Receiver.Confidence),
            payload.Config is null ? null : new ConfigSlotCandidate(
                payload.Config.Connected,
                payload.Config.Confirmed,
                payload.Config.Confidence),
            payload.AnyDesk is null ? null : new AnyDeskSlotCandidate(
                payload.AnyDesk.Address,
                payload.AnyDesk.Confidence),
            payload.Ambiguities ?? []);

    private static CustomerMessageIntent MapIntent(string? intent) =>
        intent?.ToLowerInvariant() switch
        {
            "greeting" => CustomerMessageIntent.Greeting,
            "technical_support" => CustomerMessageIntent.TechnicalSupport,
            "confirmation" => CustomerMessageIntent.TechnicalSupport,
            "rejection" => CustomerMessageIntent.TechnicalSupport,
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
        public PlayerPayload? Player { get; set; }
        public ProcessorHdmiPayload? ProcessorHdmi { get; set; }
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
        public string? Model { get; set; }
        public double Confidence { get; set; }
    }

    private sealed class PlayerPayload
    {
        public string? PlayerType { get; set; }
        public double Confidence { get; set; }
    }

    private sealed class ProcessorHdmiPayload
    {
        public bool? Connected { get; set; }
        public bool? NeedsClarification { get; set; }
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
