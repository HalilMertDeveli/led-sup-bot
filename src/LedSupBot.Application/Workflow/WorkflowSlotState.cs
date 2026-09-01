using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;

namespace LedSupBot.Application.Workflow;

public sealed class WorkflowSlotState
{
    public decimal? WidthCm { get; init; }
    public decimal? HeightCm { get; init; }
    public bool HasPanelLabelPhoto { get; init; }
    public int? ReceiverCount { get; init; }
    public bool ConfigConfirmed { get; init; }
    public bool ConfigExampleSent { get; init; }
    public bool HasAnyDesk { get; init; }
    public bool SummaryCompleted { get; init; }

    public static WorkflowSlotState FromCase(TechnicalSupportCase? supportCase, Conversation conversation)
    {
        if (supportCase is null)
        {
            return new WorkflowSlotState
            {
                SummaryCompleted = conversation.BotMode == BotMode.Human,
            };
        }

        var screen = supportCase.ScreenInformation;
        decimal? width = null;
        decimal? height = null;
        if (screen is not null)
        {
            if (screen.Width > 0)
            {
                width = screen.Width;
            }

            if (screen.Height > 0)
            {
                height = screen.Height;
            }
        }

        return new WorkflowSlotState
        {
            WidthCm = width,
            HeightCm = height,
            HasPanelLabelPhoto = supportCase.PanelLabelPhotos.Any(IsValidStoredPanelPhoto),
            ReceiverCount = supportCase.ReceiverInformation?.Count,
            ConfigConfirmed = supportCase.ConfigConnection?.Status == ConfigConnectionStatus.Confirmed,
            ConfigExampleSent = supportCase.ConfigConnection?.ExampleImageSent == true,
            HasAnyDesk = supportCase.AnyDeskInformation is not null,
            SummaryCompleted = conversation.CurrentStage is ConversationStage.HumanHandoff or ConversationStage.Completed
                || conversation.BotMode == BotMode.Human,
        };
    }

    public bool IsScreenComplete => WidthCm is > 0 && HeightCm is > 0;

    internal static bool IsValidStoredPanelPhoto(PanelLabelPhoto photo) =>
        photo.SizeBytes is > 0 &&
        !string.IsNullOrWhiteSpace(photo.StorageKey) &&
        photo.StorageKey.StartsWith("cases/", StringComparison.Ordinal);
}
