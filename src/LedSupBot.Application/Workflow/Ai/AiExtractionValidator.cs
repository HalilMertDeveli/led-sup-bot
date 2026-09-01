using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow.Parsing;
using Microsoft.Extensions.Options;

namespace LedSupBot.Application.Workflow.Ai;

/// <summary>
/// Converts AI structured output into validated slot candidates.
/// Backend deterministic rules remain authoritative — AI never bypasses validation.
/// </summary>
public sealed class AiExtractionValidator
{
    private readonly AiExtractionOptions _options;

    public AiExtractionValidator(IOptions<AiExtractionOptions> options) =>
        _options = options.Value;

    public ValidatedAiExtraction Validate(AiStructuredExtraction extraction)
    {
        var prompts = new List<string>();
        ScreenValidation? screen = null;
        int? receiverCount = null;
        bool? configConfirmed = null;
        string? anyDeskAddress = null;

        if (extraction.Screen is not null)
        {
            screen = ValidateScreen(extraction.Screen, prompts);
        }

        if (extraction.Receiver is { Count: > 0 } receiver &&
            receiver.Confidence >= _options.ConfidenceThreshold)
        {
            receiverCount = receiver.Count;
        }
        else if (extraction.Receiver is not null && extraction.Receiver.Count is not null)
        {
            prompts.Add(WorkflowPrompts.AskReceiverClarification);
        }

        if (extraction.Config is not null && extraction.Config.Confidence >= _options.ConfidenceThreshold)
        {
            if (extraction.Config.Confirmed == true || extraction.Config.Connected == true)
            {
                configConfirmed = true;
            }
            else if (extraction.Intent == CustomerMessageIntent.Rejection ||
                     extraction.Config.Confirmed == false ||
                     extraction.Config.Connected == false)
            {
                configConfirmed = false;
                prompts.Add(WorkflowPrompts.ExplainConfigConnection);
            }
        }

        if (extraction.AnyDesk is not null &&
            extraction.AnyDesk.Confidence >= _options.ConfidenceThreshold &&
            !string.IsNullOrWhiteSpace(extraction.AnyDesk.Address))
        {
            if (AnyDeskParser.TryParse(extraction.AnyDesk.Address, out var normalized))
            {
                anyDeskAddress = normalized;
            }
            else
            {
                prompts.Add(WorkflowPrompts.AskAnyDeskClarification);
            }
        }

        foreach (var ambiguity in extraction.Ambiguities)
        {
            if (ambiguity.Contains("screen", StringComparison.OrdinalIgnoreCase) ||
                ambiguity.Contains("ölçü", StringComparison.OrdinalIgnoreCase) ||
                ambiguity.Contains("olcu", StringComparison.OrdinalIgnoreCase))
            {
                prompts.Add(WorkflowPrompts.AskScreenClarification);
            }
        }

        return new ValidatedAiExtraction(
            extraction.Intent,
            screen,
            receiverCount,
            configConfirmed,
            anyDeskAddress,
            prompts.Distinct(StringComparer.Ordinal).ToList(),
            ComputeAverageConfidence(extraction));
    }

    private ScreenValidation? ValidateScreen(ScreenSlotCandidate screen, List<string> prompts)
    {
        if (screen.Confidence < _options.ConfidenceThreshold)
        {
            if (screen.WidthCm is not null || screen.HeightCm is not null ||
                screen.WidthMeters is not null || screen.HeightMeters is not null)
            {
                prompts.Add(WorkflowPrompts.AskScreenClarification);
            }

            return null;
        }

        if (screen.UnitAmbiguous)
        {
            prompts.Add(WorkflowPrompts.AskScreenClarification);
            return null;
        }

        if (screen.WidthMeters is > 0 || screen.HeightMeters is > 0)
        {
            prompts.Add(WorkflowPrompts.AskMeterConfirmation);
            return null;
        }

        decimal? width = screen.WidthCm;
        decimal? height = screen.HeightCm;

        if (width is > 0 && height is > 0)
        {
            return new ScreenValidation(width.Value, height.Value, Complete: true);
        }

        if (width is > 0 && height is null)
        {
            prompts.Add(WorkflowPrompts.AskMissingHeight);
            return new ScreenValidation(width.Value, 0, complete: false);
        }

        if (height is > 0 && width is null)
        {
            prompts.Add(WorkflowPrompts.AskMissingWidth);
            return new ScreenValidation(0, height.Value, complete: false);
        }

        prompts.Add(WorkflowPrompts.AskScreenClarification);
        return null;
    }

    private static double? ComputeAverageConfidence(AiStructuredExtraction extraction)
    {
        var values = new List<double>();
        if (extraction.Screen?.Confidence > 0)
        {
            values.Add(extraction.Screen.Confidence);
        }

        if (extraction.Receiver?.Confidence > 0)
        {
            values.Add(extraction.Receiver.Confidence);
        }

        if (extraction.Config?.Confidence > 0)
        {
            values.Add(extraction.Config.Confidence);
        }

        if (extraction.AnyDesk?.Confidence > 0)
        {
            values.Add(extraction.AnyDesk.Confidence);
        }

        return values.Count > 0 ? values.Average() : null;
    }
}

public sealed record ScreenValidation(decimal WidthCm, decimal HeightCm, bool Complete);

public sealed record ValidatedAiExtraction(
    CustomerMessageIntent Intent,
    ScreenValidation? Screen,
    int? ReceiverCount,
    bool? ConfigConfirmed,
    string? AnyDeskAddress,
    IReadOnlyList<string> ClarificationPrompts,
    double? AverageConfidence);
