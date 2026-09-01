namespace LedSupBot.Application.Workflow.Parsing;

/// <summary>
/// Canonical storage is always centimeters. Meters are converted at parse/validation time only.
/// </summary>
public static class ScreenDimensionUnitConverter
{
    public const decimal MetersToCentimetersFactor = 100m;

    public static decimal FromMeters(decimal meters) => meters * MetersToCentimetersFactor;

    public static bool IsMeterUnit(string? unit) =>
        unit is "m" or "metre" or "meter" or "metre." or "meter.";

    public static bool IsCentimeterUnit(string? unit) =>
        unit is "cm" or "centimetre" or "santimetre";
}
