using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace LedSupBot.Application.Workflow;

public sealed record HybridResponseComposeInput(
    ConversationContext Context,
    WorkflowSlotState Slots,
    ConversationStage EffectiveStage,
    WorkflowInboundMessage Inbound,
    IReadOnlyList<string> DeterministicClarifications,
    ValidatedAiResponsePlan? AiPlan,
    bool ConfigExampleAssetAvailable,
    bool ConfigExampleCompleted,
    string? ConfigExampleImagePath);

public sealed record HybridResponseComposeResult(
    IReadOnlyList<string> OutboundTexts,
    string? ConfigExampleImagePath,
    bool ConfigExampleMarkedSent);

public sealed class HybridResponseComposer
{
    private readonly ILogger<HybridResponseComposer> _logger;

    public HybridResponseComposer(ILogger<HybridResponseComposer> logger) => _logger = logger;

    public HybridResponseComposeResult Compose(HybridResponseComposeInput input)
    {
        var outbound = new List<string>();
        var configImagePath = input.ConfigExampleImagePath;
        var configExampleMarkedSent = false;

        if (input.DeterministicClarifications.Count > 0)
        {
            var filteredClarifications = input.DeterministicClarifications
                .Where(c => !(input.Slots.IsScreenComplete &&
                    (c == WorkflowPrompts.AskScreenSize ||
                     c == WorkflowPrompts.AskScreenClarification ||
                     c == WorkflowPrompts.AskMissingWidth ||
                     c == WorkflowPrompts.AskMissingHeight ||
                     c == WorkflowPrompts.AskMeterConfirmation)))
                .ToList();

            if (filteredClarifications.Count > 0)
            {
                outbound.AddRange(filteredClarifications);
                return new HybridResponseComposeResult(outbound, configImagePath, false);
            }
        }

        if (input.AiPlan?.HasUsableReply == true)
        {
            outbound.Add(input.AiPlan.Reply!);
            configExampleMarkedSent = TryAppendConfigExampleStep(input, outbound, ref configImagePath);
            return new HybridResponseComposeResult(outbound, configImagePath, configExampleMarkedSent);
        }

        if (ShouldUseGreetingFallback(input))
        {
            outbound.Add(WorkflowPrompts.NaturalGreetingNoIssue);
            return new HybridResponseComposeResult(outbound, null, false);
        }

        AppendDeterministicStageFallback(input, outbound, ref configImagePath, ref configExampleMarkedSent);
        return new HybridResponseComposeResult(outbound, configImagePath, configExampleMarkedSent);
    }

    private static bool ShouldUseGreetingFallback(HybridResponseComposeInput input)
    {
        if (input.Inbound.MessageType != MessageType.Text ||
            string.IsNullOrWhiteSpace(input.Inbound.TextContent))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(input.Context.CurrentIssue))
        {
            return false;
        }

        if (TechnicalContentHeuristic.MightContainTechnicalInfo(input.Inbound.TextContent))
        {
            return false;
        }

        return GreetingHeuristic.IsGreeting(input.Inbound.TextContent);
    }

    private bool TryAppendConfigExampleStep(
        HybridResponseComposeInput input,
        List<string> outbound,
        ref string? configImagePath)
    {
        if (input.EffectiveStage != ConversationStage.SendConfigExample ||
            input.ConfigExampleCompleted)
        {
            return false;
        }

        if (input.ConfigExampleAssetAvailable)
        {
            if (!outbound.Any(t => t.Contains("örnek", StringComparison.OrdinalIgnoreCase)))
            {
                outbound.Add(WorkflowPrompts.ConfigExampleSent);
            }

            configImagePath = input.ConfigExampleImagePath;
            return true;
        }

        if (!outbound.Any(t => t.Contains("config", StringComparison.OrdinalIgnoreCase)))
        {
            outbound.Add(WorkflowPrompts.ConfigExampleUnavailable);
            _logger.LogWarning("Config example asset not configured; step skipped deterministically");
        }

        return true;
    }

    private void AppendDeterministicStageFallback(
        HybridResponseComposeInput input,
        List<string> outbound,
        ref string? configImagePath,
        ref bool configExampleMarkedSent)
    {
        if (outbound.Any(t =>
                t.Contains("teyit", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Yatay", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Dikey", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Örnek:", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        switch (input.EffectiveStage)
        {
            case ConversationStage.AskScreenSize:
                if (!input.Slots.IsScreenComplete)
                {
                    outbound.Add(WorkflowPrompts.AskScreenSize);
                }

                break;

            case ConversationStage.AskPanelLabelPhotos:
                outbound.Add(input.Inbound.MessageType == MessageType.Text
                    ? WorkflowPrompts.RemindPanelLabelPhotos
                    : WorkflowPrompts.AskPanelLabelPhotos);
                break;

            case ConversationStage.AskReceiverCount:
                outbound.Add(
                    input.Inbound.MessageType == MessageType.Text &&
                    !string.IsNullOrWhiteSpace(input.Inbound.TextContent)
                        ? WorkflowPrompts.AskReceiverClarification
                        : WorkflowPrompts.AskReceiverCount);
                break;

            case ConversationStage.AskConfigConnection:
                outbound.Add(WorkflowPrompts.AskConfigConnection);
                break;

            case ConversationStage.SendConfigExample when !input.ConfigExampleCompleted:
                configExampleMarkedSent = TryAppendConfigExampleStep(input, outbound, ref configImagePath);
                if (configExampleMarkedSent &&
                    input.ConfigExampleAssetAvailable &&
                    input.Slots.HasAnyDesk == false)
                {
                    // After config example, resolver may advance to AnyDesk on next turn.
                }

                break;

            case ConversationStage.AskAnyDesk:
                outbound.Add(WorkflowPrompts.AskAnyDesk);
                break;
        }
    }
}
