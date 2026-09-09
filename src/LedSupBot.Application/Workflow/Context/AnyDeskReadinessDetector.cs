using LedSupBot.Domain.Enums;

namespace LedSupBot.Application.Workflow.Context;

public static class AnyDeskReadinessDetector
{
    /// <summary>
    /// Server-side AnyDesk readiness: all prior slots satisfied and workflow stage is AskAnyDesk.
    /// </summary>
    public static bool IsReadyForHandoff(WorkflowSlotState slots, ConversationStage effectiveStage) =>
        effectiveStage == ConversationStage.AskAnyDesk &&
        slots.IsScreenComplete &&
        slots.HasPanelLabelPhoto &&
        slots.ReceiverCount is > 0 &&
        slots.ConfigConfirmed &&
        !slots.HasAnyDesk;
}
