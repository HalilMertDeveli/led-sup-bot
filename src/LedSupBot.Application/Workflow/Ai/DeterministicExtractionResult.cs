using LedSupBot.Application.Workflow.Parsing;

namespace LedSupBot.Application.Workflow.Ai;

/// <summary>Outcome of Phase 5 deterministic parsers for a single inbound text message.</summary>
public sealed class DeterministicExtractionResult
{
    public bool ParsedScreen { get; init; }
    public bool ParsedReceiver { get; init; }
    public bool ParsedReceiverModel { get; init; }
    public bool ParsedPlayerType { get; init; }
    public bool ParsedProcessorHdmi { get; init; }
    public bool ParsedConfig { get; init; }
    public bool ParsedAnyDesk { get; init; }
    public bool ScreenNeedsConfirmation { get; init; }
    public bool ScreenAmbiguous { get; init; }
    public bool ScreenPartial { get; init; }
    public bool ScreenComplete { get; init; }

    public bool IsFullyConfident =>
        (ParsedScreen && ScreenComplete && !ScreenNeedsConfirmation && !ScreenAmbiguous) ||
        ParsedReceiver ||
        ParsedReceiverModel ||
        ParsedPlayerType ||
        ParsedProcessorHdmi ||
        ParsedConfig ||
        ParsedAnyDesk;

    public bool HasUnresolvedPotential =>
        ScreenNeedsConfirmation ||
        ScreenAmbiguous ||
        ScreenPartial ||
        (!ParsedScreen && TechnicalContentHeuristic.MightContainScreenInfo(Text)) ||
        (!ParsedReceiver && !ParsedReceiverModel && TechnicalContentHeuristic.MightContainReceiverInfo(Text)) ||
        (!ParsedPlayerType && TechnicalContentHeuristic.MightContainPlayerInfo(Text)) ||
        (!ParsedProcessorHdmi && TechnicalContentHeuristic.MightContainHdmiInfo(Text)) ||
        (!ParsedConfig && TechnicalContentHeuristic.MightContainConfigInfo(Text)) ||
        (!ParsedAnyDesk && TechnicalContentHeuristic.MightContainAnyDeskInfo(Text));

    public string Text { get; init; } = string.Empty;

    public static DeterministicExtractionResult Analyze(string text)
    {
        var screen = ScreenSizeParser.Parse(text);
        var parsedReceiver = ReceiverCountParser.TryParse(text, out _);
        var parsedReceiverModel = ReceiverModelParser.TryParse(text, out _);
        var parsedPlayerType = PlayerTypeParser.TryParse(text, out _);
        var parsedProcessorHdmi = ProcessorHdmiParser.Parse(text) != ProcessorHdmiParseResult.None;
        var config = ConfigConfirmationParser.Parse(text);
        var parsedAnyDesk = AnyDeskParser.TryParse(text, out _);

        var screenComplete = screen.WidthCm is > 0 && screen.HeightCm is > 0;
        var screenPartial = (screen.WidthCm is > 0 && screen.HeightCm is null) ||
                            (screen.HeightCm is > 0 && screen.WidthCm is null);

        return new DeterministicExtractionResult
        {
            Text = text,
            ParsedScreen = screen.ParsedAny,
            ParsedReceiver = parsedReceiver,
            ParsedReceiverModel = parsedReceiverModel,
            ParsedPlayerType = parsedPlayerType,
            ParsedProcessorHdmi = parsedProcessorHdmi,
            ParsedConfig = config == ConfigConfirmationResult.Confirmed,
            ParsedAnyDesk = parsedAnyDesk,
            ScreenNeedsConfirmation = screen.NeedsMeterConfirmation,
            ScreenAmbiguous = screen.Ambiguous,
            ScreenPartial = screenPartial,
            ScreenComplete = screenComplete,
        };
    }
}
