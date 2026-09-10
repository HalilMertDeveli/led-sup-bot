using LedSupBot.Domain.Enums;

namespace LedSupBot.Application.Abstractions;

/// <summary>
/// AI understanding/extraction boundary. Extraction-only — must not own conversation state or send messages.
/// </summary>
public interface IAiService
{
    Task<AiStructuredExtraction> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default);
}

public sealed record AiExtractionRequest(
    string MessageText,
    string? LanguageHint,
    IReadOnlyDictionary<string, string?> KnownSlots);

public enum ConversationIntent
{
    Unclear = 0,
    WorkflowInformation = 1,
    QuestionAboutCurrentStep = 2,
    LedTechnicalQuestion = 3,
    LedTroubleshooting = 4,
    Correction = 5,
    Acknowledgement = 6,
    FlowRecovery = 7,
    ResumeWorkflow = 8,
    CasualConversation = 9,
    GeneralKnowledge = 10,
    NewCase = 11,
    ScreenSetup = 12,
    FileRequest = 13,
    LedTechnicalSupport = 14,
}

// Compatibility boundary for the untouched OpenAI fallback. Production workflow code uses ConversationIntent.
public enum CustomerMessageIntent
{
    Unknown = 0,
    Greeting = 1,
    TechnicalSupport = 2,
    Confirmation = 3,
    Rejection = 4,
    AskClarification = 5,
    AskTechnicalQuestion = 6,
    Correction = 7,
    Acknowledgement = 8,
}

public sealed record ScreenSlotCandidate(
    decimal? WidthCm,
    decimal? HeightCm,
    decimal? WidthMeters,
    decimal? HeightMeters,
    double Confidence,
    bool UnitAmbiguous);

public sealed record ReceiverSlotCandidate(
    int? Count,
    double Confidence,
    string? Model = null,
    string? Manufacturer = null,
    ReceiverModel? ReceiverModel = null)
{
    public ReceiverSlotCandidate(int? count, string? model, double confidence, string? manufacturer = null, ReceiverModel? receiverModel = null)
        : this(count, confidence, model, manufacturer, receiverModel) { }
}

public sealed record PlayerSlotCandidate(
    string? PlayerTypeString,
    double Confidence,
    PlayerType? PlayerType = null)
{
    public PlayerSlotCandidate(PlayerType? playerType, double confidence)
        : this(playerType?.ToString(), confidence, playerType) { }
}

public sealed record ProcessorHdmiSlotCandidate(
    bool? Connected,
    double Confidence,
    bool? NeedsClarification = null);

public sealed record ConfigSlotCandidate(
    bool? Connected,
    bool? Confirmed,
    double Confidence,
    bool? ClarificationRequested = null);

public sealed record AnyDeskSlotCandidate(
    string? Address,
    double Confidence);

/// <summary>Structured extraction result from AI. Backend validation remains authoritative.</summary>
public sealed record AiStructuredExtraction(
    ConversationIntent Intent,
    ScreenSlotCandidate? Screen,
    ReceiverSlotCandidate? Receiver,
    ConfigSlotCandidate? Config,
    AnyDeskSlotCandidate? AnyDesk,
    IReadOnlyList<string> Ambiguities,
    string? CustomerQuestion = null,
    PlayerSlotCandidate? Player = null,
    ProcessorHdmiSlotCandidate? ProcessorHdmi = null)
{
    public AiStructuredExtraction(
        CustomerMessageIntent Intent,
        ScreenSlotCandidate? Screen,
        ReceiverSlotCandidate? Receiver,
        ConfigSlotCandidate? Config,
        AnyDeskSlotCandidate? AnyDesk,
        IReadOnlyList<string> Ambiguities,
        string? CustomerQuestion = null,
        PlayerSlotCandidate? Player = null,
        ProcessorHdmiSlotCandidate? ProcessorHdmi = null)
        : this(Intent switch
        {
            CustomerMessageIntent.Greeting => ConversationIntent.CasualConversation,
            CustomerMessageIntent.TechnicalSupport or CustomerMessageIntent.Confirmation or CustomerMessageIntent.Rejection => ConversationIntent.WorkflowInformation,
            CustomerMessageIntent.AskClarification => ConversationIntent.QuestionAboutCurrentStep,
            CustomerMessageIntent.AskTechnicalQuestion => ConversationIntent.LedTechnicalQuestion,
            CustomerMessageIntent.Correction => ConversationIntent.Correction,
            CustomerMessageIntent.Acknowledgement => ConversationIntent.Acknowledgement,
            _ => ConversationIntent.Unclear,
        }, Screen, Receiver, Config, AnyDesk, Ambiguities, CustomerQuestion, Player, ProcessorHdmi)
    { }
}
