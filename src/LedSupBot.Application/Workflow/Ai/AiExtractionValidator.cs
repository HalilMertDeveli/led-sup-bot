using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow.Parsing;
using LedSupBot.Domain.Enums;
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
        ReceiverModel? receiverModel = null;
        PlayerType? playerType = null;
        bool? processorHdmiConnected = null;
        bool? processorHdmiNeedsClarification = null;
        bool? configConfirmed = null;
        bool configClarificationRequested = false;
        string? anyDeskAddress = null;

        if (extraction.Screen is not null)
        {
            screen = ValidateScreen(extraction.Screen, prompts);
        }

        if (extraction.Receiver is not null && extraction.Receiver.Confidence >= _options.ConfidenceThreshold)
        {
            if (extraction.Receiver.Count is > 0)
            {
                receiverCount = extraction.Receiver.Count;
            }

            if (extraction.Receiver.ReceiverModel is not null and not ReceiverModel.Unknown)
            {
                receiverModel = extraction.Receiver.ReceiverModel;
            }
            else if (!string.IsNullOrWhiteSpace(extraction.Receiver.Model))
            {
                var parsed = ReceiverModelParser.Normalize(extraction.Receiver.Model);
                if (parsed != ReceiverModel.Unknown)
                {
                    receiverModel = parsed;
                }
            }
        }
        else if (extraction.Receiver is not null && extraction.Receiver.Count is not null)
        {
            prompts.Add(WorkflowPrompts.AskReceiverClarification);
        }

        if (extraction.Player is not null && extraction.Player.Confidence >= _options.ConfidenceThreshold)
        {
            if (extraction.Player.PlayerType is not null and not PlayerType.Unknown)
            {
                playerType = extraction.Player.PlayerType;
            }
        }

        if (extraction.ProcessorHdmi is not null && extraction.ProcessorHdmi.Confidence >= _options.ConfidenceThreshold)
        {
            if (extraction.ProcessorHdmi.Connected == true)
            {
                processorHdmiConnected = true;
            }
            else if (extraction.ProcessorHdmi.Connected == false)
            {
                processorHdmiConnected = false;
            }
            processorHdmiNeedsClarification = extraction.ProcessorHdmi.NeedsClarification;
        }

        if (extraction.Config is not null && extraction.Config.Confidence >= _options.ConfidenceThreshold)
        {
            if (extraction.Config.Confirmed == true || extraction.Config.Connected == true)
            {
                configConfirmed = true;
            }
            else if (extraction.Config.ClarificationRequested == true || extraction.Intent == ConversationIntent.QuestionAboutCurrentStep)
            {
                configClarificationRequested = true;
            }
            else if (extraction.Intent == ConversationIntent.WorkflowInformation ||
                     extraction.Config.Confirmed == false ||
                     extraction.Config.Connected == false)
            {
                configConfirmed = false;
                prompts.Add(WorkflowPrompts.ExplainConfigConnection);
            }
        }
        else if (extraction.Intent == ConversationIntent.QuestionAboutCurrentStep)
        {
            configClarificationRequested = true;
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
            ComputeAverageConfidence(extraction),
            receiverModel,
            playerType,
            processorHdmiConnected,
            processorHdmiNeedsClarification,
            configClarificationRequested,
            extraction.CustomerQuestion);
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

        var widthCm = ResolveWidthCm(screen);
        var heightCm = ResolveHeightCm(screen);

        if (widthCm is > 0 && heightCm is > 0)
        {
            return new ScreenValidation(widthCm.Value, heightCm.Value, true);
        }

        if (widthCm is > 0 && heightCm is null)
        {
            prompts.Add(WorkflowPrompts.AskMissingHeight);
            return new ScreenValidation(widthCm.Value, 0, false);
        }

        if (heightCm is > 0 && widthCm is null)
        {
            prompts.Add(WorkflowPrompts.AskMissingWidth);
            return new ScreenValidation(0, heightCm.Value, false);
        }

        if (screen.WidthMeters is not null || screen.HeightMeters is not null ||
            screen.WidthCm is not null || screen.HeightCm is not null)
        {
            prompts.Add(WorkflowPrompts.AskScreenClarification);
        }

        return null;
    }

    private static decimal? ResolveWidthCm(ScreenSlotCandidate screen)
    {
        if (screen.WidthCm is > 0)
        {
            return Math.Round(screen.WidthCm.Value, 2);
        }

        if (screen.WidthMeters is > 0)
        {
            return Math.Round(ScreenDimensionUnitConverter.FromMeters(screen.WidthMeters.Value), 2);
        }

        return null;
    }

    private static decimal? ResolveHeightCm(ScreenSlotCandidate screen)
    {
        if (screen.HeightCm is > 0)
        {
            return Math.Round(screen.HeightCm.Value, 2);
        }

        if (screen.HeightMeters is > 0)
        {
            return Math.Round(ScreenDimensionUnitConverter.FromMeters(screen.HeightMeters.Value), 2);
        }

        return null;
    }

    private static double? ComputeAverageConfidence(AiStructuredExtraction extraction)
    {
        var values = new List<double>();
        if (extraction.Screen is not null)
        {
            values.Add(extraction.Screen.Confidence);
        }

        if (extraction.Receiver is not null)
        {
            values.Add(extraction.Receiver.Confidence);
        }

        if (extraction.Config is not null)
        {
            values.Add(extraction.Config.Confidence);
        }

        if (extraction.AnyDesk is not null)
        {
            values.Add(extraction.AnyDesk.Confidence);
        }

        return values.Count > 0 ? values.Average() : null;
    }
}

public sealed record ScreenValidation(decimal WidthCm, decimal HeightCm, bool Complete);

public sealed record ValidatedAiExtraction(
    ConversationIntent Intent,
    ScreenValidation? Screen,
    int? ReceiverCount,
    bool? ConfigConfirmed,
    string? AnyDeskAddress,
    IReadOnlyList<string> ClarificationPrompts,
    double? AverageConfidence,
    ReceiverModel? ReceiverModel = null,
    PlayerType? PlayerType = null,
    bool? ProcessorHdmiConnected = null,
    bool? ProcessorHdmiNeedsClarification = null,
    bool ConfigClarificationRequested = false,
    string? CustomerQuestion = null);
