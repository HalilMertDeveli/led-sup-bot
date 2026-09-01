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

public enum CustomerMessageIntent
{
    Unknown = 0,
    Greeting = 1,
    TechnicalSupport = 2,
    Confirmation = 3,
    Rejection = 4,
}

public sealed record ScreenSlotCandidate(
    decimal? WidthCm,
    decimal? HeightCm,
    decimal? WidthMeters,
    decimal? HeightMeters,
    double Confidence,
    bool UnitAmbiguous);

public sealed record ReceiverSlotCandidate(int? Count, double Confidence);

public sealed record ConfigSlotCandidate(bool? Connected, bool? Confirmed, double Confidence);

public sealed record AnyDeskSlotCandidate(string? Address, double Confidence);

/// <summary>Structured extraction result from OpenAI. Backend validation remains authoritative.</summary>
public sealed record AiStructuredExtraction(
    CustomerMessageIntent Intent,
    ScreenSlotCandidate? Screen,
    ReceiverSlotCandidate? Receiver,
    ConfigSlotCandidate? Config,
    AnyDeskSlotCandidate? AnyDesk,
    IReadOnlyList<string> Ambiguities);
