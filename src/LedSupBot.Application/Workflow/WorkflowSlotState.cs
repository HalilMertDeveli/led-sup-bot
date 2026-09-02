using System.Collections.Generic;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Application.Workflow.Parsing;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;

namespace LedSupBot.Application.Workflow;

public sealed class WorkflowSlotState
{
    public decimal? WidthCm { get; init; }
    public decimal? HeightCm { get; init; }
    public bool HasPanelLabelPhoto { get; init; }
    public int? ReceiverCount { get; init; }
    public ReceiverModel? ReceiverModel { get; init; }
    public PlayerType? PlayerType { get; init; }
    public bool? ProcessorHdmiConnected { get; init; }
    public bool ConfigConfirmed { get; init; }
    public bool ConfigExampleSent { get; init; }
    public bool HasAnyDesk { get; init; }
    public bool SummaryCompleted { get; init; }
    public IReadOnlyDictionary<string, int> QuestionAttempts { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

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

        ReceiverModel? receiverModel = null;
        if (supportCase.ReceiverInformation?.ReceiverModel is not null && supportCase.ReceiverInformation.ReceiverModel != Domain.Enums.ReceiverModel.Unknown)
        {
            receiverModel = supportCase.ReceiverInformation.ReceiverModel;
        }
        else if (!string.IsNullOrWhiteSpace(supportCase.ReceiverInformation?.Model))
        {
            if (ReceiverModelParser.TryParse(supportCase.ReceiverInformation.Model, out var parsed) && parsed != Domain.Enums.ReceiverModel.Unknown)
            {
                receiverModel = parsed;
            }
            else
            {
                var normalized = ReceiverModelParser.Normalize(supportCase.ReceiverInformation.Model);
                if (normalized != Domain.Enums.ReceiverModel.Unknown)
                {
                    receiverModel = normalized;
                }
            }
        }

        return new WorkflowSlotState
        {
            WidthCm = width,
            HeightCm = height,
            HasPanelLabelPhoto = supportCase.PanelLabelPhotos.Any(IsValidStoredPanelPhoto),
            ReceiverCount = supportCase.ReceiverInformation?.Count > 0 ? supportCase.ReceiverInformation.Count : null,
            ReceiverModel = receiverModel,
            PlayerType = supportCase.PlayerType != Domain.Enums.PlayerType.Unknown ? supportCase.PlayerType : null,
            ProcessorHdmiConnected = supportCase.ProcessorHdmiConnected,
            ConfigConfirmed = supportCase.ConfigConnection?.Status == ConfigConnectionStatus.Confirmed,
            ConfigExampleSent = supportCase.ConfigConnection?.ExampleImageSent == true,
            HasAnyDesk = supportCase.AnyDeskInformation is not null,
            SummaryCompleted = conversation.CurrentStage is ConversationStage.HumanHandoff or ConversationStage.Completed
                || conversation.BotMode == BotMode.Human,
            QuestionAttempts = supportCase.GetAllQuestionAttempts(),
        };
    }

    public bool IsScreenComplete => WidthCm is > 0 && HeightCm is > 0;

    public bool IsReceiverCountKnown => ReceiverCount is > 0;

    public bool IsReceiverModelKnown => ReceiverModel is not null and not Domain.Enums.ReceiverModel.Unknown;

    public bool IsReceiverComplete => IsReceiverCountKnown && IsReceiverModelKnown;

    public bool IsPlayerComplete => PlayerType is not null and not Domain.Enums.PlayerType.Unknown;

    public bool IsProcessorHdmiComplete => PlayerType != Domain.Enums.PlayerType.Processor || ProcessorHdmiConnected == true;

    public int GetAttempts(string fact) =>
        QuestionAttempts.TryGetValue(fact, out var count) ? count : 0;

    public bool IsFactDeferred(string fact) => GetAttempts(fact) >= 2;

    public bool IsScreenDeferred => IsFactDeferred("ScreenDimensions");
    public bool IsPanelPhotoDeferred => IsFactDeferred("PanelLabel");
    public bool IsReceiverCountDeferred => IsFactDeferred("ReceiverCount");
    public bool IsReceiverModelDeferred => IsFactDeferred("ReceiverModel");
    public bool IsPlayerTypeDeferred => IsFactDeferred("PlayerType");
    public bool IsProcessorHdmiDeferred => IsFactDeferred("ProcessorHdmi");
    public bool IsConfigConnectionDeferred => IsFactDeferred("ConfigConnection");
    public bool IsAnyDeskDeferred => IsFactDeferred("AnyDesk");

    public bool IsFactKnown(PendingWorkflowFact fact) => fact switch
    {
        PendingWorkflowFact.ScreenDimensions => IsScreenComplete,
        PendingWorkflowFact.PanelLabel => HasPanelLabelPhoto,
        PendingWorkflowFact.ReceiverCount => IsReceiverCountKnown,
        PendingWorkflowFact.ReceiverModel => IsReceiverModelKnown,
        PendingWorkflowFact.PlayerType => IsPlayerComplete,
        PendingWorkflowFact.ProcessorHdmi => IsProcessorHdmiComplete,
        PendingWorkflowFact.ConfigConnection => ConfigConfirmed,
        PendingWorkflowFact.AnyDesk => HasAnyDesk,
        _ => false,
    };

    internal static bool IsValidStoredPanelPhoto(PanelLabelPhoto photo) =>
        photo.SizeBytes is > 0 &&
        !string.IsNullOrWhiteSpace(photo.StorageKey) &&
        photo.StorageKey.StartsWith("cases/", StringComparison.Ordinal);
}
