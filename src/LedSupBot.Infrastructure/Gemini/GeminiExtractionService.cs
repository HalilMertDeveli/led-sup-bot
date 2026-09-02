using System.Text.Json;
using System.Text.Json.Serialization;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow.Parsing;
using LedSupBot.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LedSupBot.Infrastructure.Gemini;

public sealed class GeminiExtractionService : IAiService
{
    public const string PromptVersion = "gemini-v2";

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

    public async Task<AiStructuredExtraction> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default)
    {
        var rawJson = await _client.TryGenerateJsonAsync(
            "slot_extraction",
            BuildSystemPrompt(),
            BuildUserPrompt(request),
            GeminiJsonSchemas.Extraction,
            temperature: 0.1,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return new AiStructuredExtraction(
                ConversationIntent.Unclear,
                Screen: null,
                Receiver: null,
                Config: null,
                AnyDesk: null,
                Ambiguities: ["gemini_empty_response"]);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<GeminiExtractionPayload>(rawJson, JsonOptions);
            if (parsed is null)
            {
                return new AiStructuredExtraction(
                    ConversationIntent.Unclear,
                    Screen: null,
                    Receiver: null,
                    Config: null,
                    AnyDesk: null,
                    Ambiguities: ["gemini_deserialization_null"]);
            }

            return MapToDomain(parsed);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Gemini JSON deserialization failed: {Raw}", rawJson);
            return new AiStructuredExtraction(
                ConversationIntent.Unclear,
                Screen: null,
                Receiver: null,
                Config: null,
                AnyDesk: null,
                Ambiguities: ["gemini_json_invalid"]);
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
            payload.Receiver is null ? null : new ReceiverSlotCandidate(
                payload.Receiver.Count,
                payload.Receiver.Confidence,
                payload.Receiver.Model,
                payload.Receiver.Manufacturer),
            payload.Config is null ? null : new ConfigSlotCandidate(
                payload.Config.Connected,
                payload.Config.Confirmed,
                payload.Config.Confidence,
                payload.Config.ClarificationRequested),
            payload.AnyDesk is null ? null : new AnyDeskSlotCandidate(
                payload.AnyDesk.Address,
                payload.AnyDesk.Confidence),
            payload.Ambiguities ?? [],
            payload.CustomerQuestion,
            payload.Player is null ? null : new PlayerSlotCandidate(
                payload.Player.PlayerType,
                payload.Player.Confidence,
                PlayerTypeParser.Normalize(payload.Player.PlayerType)),
            payload.ProcessorHdmi is null ? null : new ProcessorHdmiSlotCandidate(
                payload.ProcessorHdmi.Connected,
                payload.ProcessorHdmi.Confidence,
                payload.ProcessorHdmi.NeedsClarification));

    private static ConversationIntent MapIntent(string? intent) =>
        intent?.ToLowerInvariant() switch
        {
            "greeting" => ConversationIntent.CasualConversation,
            "technical_support" => ConversationIntent.WorkflowInformation,
            "confirmation" => ConversationIntent.WorkflowInformation,
            "rejection" => ConversationIntent.WorkflowInformation,
            "ask_clarification" => ConversationIntent.QuestionAboutCurrentStep,
            "ask_technical_question" => ConversationIntent.LedTechnicalQuestion,
            "general_technical_question" => ConversationIntent.LedTechnicalQuestion,
            "led_troubleshooting" => ConversationIntent.LedTroubleshooting,
            "workflow_information" => ConversationIntent.WorkflowInformation,
            "customer_question_about_current_step" => ConversationIntent.QuestionAboutCurrentStep,
            "flow_recovery" => ConversationIntent.FlowRecovery,
            "return_to_workflow" or "resume_workflow" => ConversationIntent.ResumeWorkflow,
            "casual_conversation" => ConversationIntent.CasualConversation,
            "general_knowledge" => ConversationIntent.GeneralKnowledge,
            "correction" => ConversationIntent.Correction,
            "acknowledgement" => ConversationIntent.Acknowledgement,
            _ => ConversationIntent.Unclear,
        };

    private sealed class GeminiExtractionPayload
    {
        public string? Intent { get; set; }
        public ScreenPayload? Screen { get; set; }
        public ReceiverPayload? Receiver { get; set; }
        public PlayerPayload? Player { get; set; }
        public ProcessorHdmiPayload? ProcessorHdmi { get; set; }
        public ConfigPayload? Config { get; set; }
        public AnyDeskPayload? AnyDesk { get; set; }
        public string? CustomerQuestion { get; set; }
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
        public string? Manufacturer { get; set; }
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
        public bool? ClarificationRequested { get; set; }
        public double Confidence { get; set; }
    }

    private sealed class AnyDeskPayload
    {
        public string? Address { get; set; }
        public double Confidence { get; set; }
    }
}
