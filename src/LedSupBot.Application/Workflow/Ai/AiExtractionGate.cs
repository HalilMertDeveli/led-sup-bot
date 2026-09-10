using System.Text.RegularExpressions;

namespace LedSupBot.Application.Workflow.Ai;

public static class AiExtractionGate
{
    /// <summary>
    /// Cost control: invoke OpenAI/AI extraction only when text may contain technical info
    /// and deterministic parsers did not fully resolve it or text is a complex conversational message.
    /// </summary>
    public static bool ShouldInvokeAi(string? text, DeterministicExtractionResult deterministic)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!TechnicalContentHeuristic.MightContainTechnicalInfo(text))
        {
            return false;
        }

        // Complex conversational multi-slot messages with colloquial phrases
        if (IsComplexConversationalUtterance(text))
        {
            return true;
        }

        if (deterministic.IsFullyConfident && !deterministic.HasUnresolvedPotential)
        {
            return false;
        }

        if (deterministic.HasUnresolvedPotential)
        {
            return true;
        }

        return false;
    }

    private static bool IsComplexConversationalUtterance(string text) =>
        Regex.IsMatch(
            text,
            @"\b(?:abi|hocam|usta|arkada|ekranımız|ekranimiz|bizim\s+ekran)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
