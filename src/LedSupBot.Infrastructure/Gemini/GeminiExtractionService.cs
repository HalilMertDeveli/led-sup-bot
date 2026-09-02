using System.Text.Json;
using System.Text.Json.Serialization;
using LedSupBot.Application.Abstractions;
using LedSupBot.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LedSupBot.Infrastructure.Gemini;

public sealed class GeminiExtractionService : IAiService
{
    public const string PromptVersion = "gemini-v1";

    private readonly GeminiStructuredClient _client;
    private readonly ILogger<GeminiExtractionService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public GeminiExtractionService(
        GeminiStructuredClient client,
        ILogger<GeminiExtractionService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<AiStructuredExtraction> ExtractAsync(
        AiExtractionRequest request,
        CancellationToken cancellationToken = default)
    {
        var content = await _client.TryGenerateJsonAsync(
            "extraction",
            BuildSystemPrompt(),
            BuildUserPrompt(request),
            GeminiJsonSchemas.Extraction,
            temperature: 0,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Gemini extraction unavailable.");
        }

        GeminiExtractionPayload? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<GeminiExtractionPayload>(content, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Gemini extraction JSON invalid; using deterministic fallback");
            throw new InvalidOperationException("Gemini extraction JSON invalid.", ex);
        }

        if (parsed is null)
        {
            throw new InvalidOperationException("Gemini extraction payload empty.");
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
        - Do not include secrets, phone numbers, or full JIDs.
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

    private static AiStructuredExtraction MapToDomain(GeminiExtractionPayload payload) =>
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

    private sealed class GeminiExtractionPayload
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
