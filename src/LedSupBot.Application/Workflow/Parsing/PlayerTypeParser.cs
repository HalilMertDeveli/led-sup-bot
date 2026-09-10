using System.Text.RegularExpressions;
using LedSupBot.Domain.Enums;

namespace LedSupBot.Application.Workflow.Parsing;

public static partial class PlayerTypeParser
{
    public static bool TryParse(string? text, out PlayerType playerType)
    {
        playerType = PlayerType.Unknown;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim();

        // Check correction e.g. "playbox değil processor"
        var correction = CorrectionPattern().Match(normalized);
        if (correction.Success)
        {
            var target = correction.Groups[1].Value;
            if (TryParseSingleToken(target, out playerType))
            {
                return true;
            }
        }

        if (PlayboxPattern().IsMatch(normalized))
        {
            playerType = PlayerType.Playbox;
            return true;
        }

        if (ProcessorPattern().IsMatch(normalized))
        {
            playerType = PlayerType.Processor;
            return true;
        }

        return false;
    }

    public static PlayerType Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return PlayerType.Unknown;
        }

        if (TryParse(value, out var playerType))
        {
            return playerType;
        }

        var cleaned = value.Trim().ToLowerInvariant();
        if (cleaned.Contains("playbox") || cleaned.Contains("play box") || cleaned.Contains("pleybox"))
        {
            return PlayerType.Playbox;
        }

        if (cleaned.Contains("processor") || cleaned.Contains("prosesör") || cleaned.Contains("prossesor") || cleaned.Contains("işlemci") || cleaned.Contains("islemci"))
        {
            return PlayerType.Processor;
        }

        return PlayerType.Unknown;
    }

    public static bool TryParseSingleToken(string token, out PlayerType playerType)
    {
        var cleaned = token.Trim().ToLowerInvariant();
        if (cleaned.Contains("playbox") || cleaned.Contains("play box") || cleaned.Contains("pleybox"))
        {
            playerType = PlayerType.Playbox;
            return true;
        }

        if (cleaned.Contains("processor") || cleaned.Contains("prosesör") || cleaned.Contains("prossesor") || cleaned.Contains("işlemci") || cleaned.Contains("islemci"))
        {
            playerType = PlayerType.Processor;
            return true;
        }

        playerType = PlayerType.Unknown;
        return false;
    }

    public static string ToDisplayString(PlayerType playerType) => playerType switch
    {
        PlayerType.Playbox => "Playbox",
        PlayerType.Processor => "Processor",
        _ => ""
    };

    [GeneratedRegex(@"değil(?:dir)?\s*,?\s*(play\s*box|playbox|pleybox|processor|prosesör|prossesor|işlemci|islemci)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CorrectionPattern();

    [GeneratedRegex(@"\b(?:play\s*box|playbox|pleybox)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlayboxPattern();

    [GeneratedRegex(@"\b(?:processor|prosesör|prossesor|prossesör|işlemci|islemci)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProcessorPattern();
}
