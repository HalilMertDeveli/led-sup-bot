namespace LedSupBot.Application.Workflow.Parsing;

public sealed class ScreenSizeParseResult
{
    public bool ParsedAny { get; init; }
    public decimal? WidthCm { get; init; }
    public decimal? HeightCm { get; init; }
    public bool NeedsMeterConfirmation { get; init; }
    public bool Ambiguous { get; init; }

    public static ScreenSizeParseResult Empty { get; } = new();
}
