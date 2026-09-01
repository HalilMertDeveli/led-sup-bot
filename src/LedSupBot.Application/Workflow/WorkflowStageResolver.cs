using LedSupBot.Domain.Enums;

namespace LedSupBot.Application.Workflow;

public static class WorkflowStageResolver
{
    public static ConversationStage ResolveEffectiveStage(WorkflowSlotState slots, bool configExampleAssetAvailable, bool configExampleStepCompleted)
    {
        if (slots.SummaryCompleted)
        {
            return ConversationStage.HumanHandoff;
        }

        if (!slots.IsScreenComplete)
        {
            return ConversationStage.AskScreenSize;
        }

        if (!slots.HasPanelLabelPhoto)
        {
            return ConversationStage.AskPanelLabelPhotos;
        }

        if (slots.ReceiverCount is null or <= 0)
        {
            return ConversationStage.AskReceiverCount;
        }

        if (!slots.ConfigConfirmed)
        {
            return ConversationStage.AskConfigConnection;
        }

        if (!configExampleStepCompleted)
        {
            return ConversationStage.SendConfigExample;
        }

        if (!slots.HasAnyDesk)
        {
            return ConversationStage.AskAnyDesk;
        }

        return ConversationStage.CollectingFinalInfo;
    }
}
