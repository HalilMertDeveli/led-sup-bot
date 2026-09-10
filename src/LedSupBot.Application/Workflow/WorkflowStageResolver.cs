using LedSupBot.Domain.Enums;

namespace LedSupBot.Application.Workflow;

public static class WorkflowStageResolver
{
    public static ConversationStage ResolveEffectiveStage(WorkflowSlotState slots, bool configExampleAssetAvailable, bool configExampleStepCompleted,
        bool remoteSupportAvailable = true)
    {
        if (slots.SummaryCompleted)
        {
            return ConversationStage.HumanHandoff;
        }

        if (!slots.IsScreenComplete && !slots.IsScreenDeferred)
        {
            return ConversationStage.AskScreenSize;
        }

        if (!slots.HasPanelLabelPhoto && !slots.IsPanelPhotoDeferred)
        {
            return ConversationStage.AskPanelLabelPhotos;
        }

        if (!slots.IsReceiverCountKnown && !slots.IsReceiverCountDeferred)
        {
            return ConversationStage.AskReceiverCount;
        }

        if (!slots.IsReceiverModelKnown && !slots.IsReceiverModelDeferred)
        {
            return ConversationStage.AskReceiverModel;
        }

        if (!slots.IsPlayerComplete && !slots.IsPlayerTypeDeferred)
        {
            return ConversationStage.AskPlayerType;
        }

        if (slots.PlayerType == PlayerType.Processor && !slots.IsProcessorHdmiComplete && !slots.IsProcessorHdmiDeferred)
        {
            return ConversationStage.AskProcessorHdmi;
        }

        if (!slots.ConfigConfirmed && !slots.IsConfigConnectionDeferred)
        {
            return ConversationStage.AskConfigConnection;
        }

        if (remoteSupportAvailable && !slots.HasAnyDesk && !slots.IsAnyDeskDeferred)
        {
            return ConversationStage.AskAnyDesk;
        }

        return ConversationStage.CollectingFinalInfo;
    }

    public static Context.PendingWorkflowFact ResolvePendingFact(ConversationStage stage) => stage switch
    {
        ConversationStage.AskScreenSize => Context.PendingWorkflowFact.ScreenDimensions,
        ConversationStage.AskPanelLabelPhotos => Context.PendingWorkflowFact.PanelLabel,
        ConversationStage.AskReceiverCount => Context.PendingWorkflowFact.ReceiverCount,
        ConversationStage.AskReceiverModel => Context.PendingWorkflowFact.ReceiverModel,
        ConversationStage.AskPlayerType => Context.PendingWorkflowFact.PlayerType,
        ConversationStage.AskProcessorHdmi => Context.PendingWorkflowFact.ProcessorHdmi,
        ConversationStage.AskConfigConnection => Context.PendingWorkflowFact.ConfigConnection,
        ConversationStage.AskAnyDesk => Context.PendingWorkflowFact.AnyDesk,
        _ => Context.PendingWorkflowFact.None,
    };
}
