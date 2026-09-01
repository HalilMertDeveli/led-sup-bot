namespace LedSupBot.Application.Workflow.Ai;

public static class AiExtractionGate
{
    /// <summary>
    /// Cost control: invoke OpenAI only when text may contain technical info
    /// and deterministic parsers did not fully resolve it.
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

        if (deterministic.IsFullyConfident && !deterministic.HasUnresolvedPotential)
        {
            return false;
        }

        if (deterministic.ScreenNeedsConfirmation || deterministic.ScreenAmbiguous)
        {
            return true;
        }

        if (deterministic.ScreenPartial)
        {
            return true;
        }

        if (!deterministic.ParsedScreen && TechnicalContentHeuristic.MightContainScreenInfo(text))
        {
            return true;
        }

        if (!deterministic.ParsedReceiver && TechnicalContentHeuristic.MightContainReceiverInfo(text))
        {
            return true;
        }

        if (!deterministic.ParsedConfig && TechnicalContentHeuristic.MightContainConfigInfo(text))
        {
            return true;
        }

        if (!deterministic.ParsedAnyDesk && TechnicalContentHeuristic.MightContainAnyDeskInfo(text))
        {
            return true;
        }

        return deterministic.ParsedScreen && deterministic.ParsedReceiver == false &&
               TechnicalContentHeuristic.MightContainReceiverInfo(text);
    }
}
