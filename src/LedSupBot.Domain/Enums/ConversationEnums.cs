namespace LedSupBot.Domain.Enums;

/// <summary>
/// Slot-driven conversation stage. Workflow transitions belong to Phase 5.
/// </summary>
public enum ConversationStage
{
    Start = 0,
    Greeting = 1,
    AskScreenSize = 2,
    AskPanelLabelPhotos = 3,
    AskReceiverCount = 4,
    AskConfigConnection = 5,
    SendConfigExample = 6,
    AskAnyDesk = 7,
    CollectingFinalInfo = 8,
    HumanHandoff = 9,
    Completed = 10,
    AskReceiverModel = 11,
    AskPlayerType = 12,
    AskProcessorHdmi = 13
}

public enum ReceiverModel
{
    Unknown = 0,
    E120 = 1,
    E80 = 2,
    _5A75E = 3,
    E320Pro = 4
}

public enum PlayerType
{
    Unknown = 0,
    Playbox = 1,
    Processor = 2
}

public enum ConversationStatus
{
    Active = 0,
    WaitingTechnician = 1,
    InHumanSupport = 2,
    Completed = 3,
    Abandoned = 4
}

/// <summary>
/// Bot auto-reply mode. Human silences automated responses (behavior in Phase 8).
/// </summary>
public enum BotMode
{
    Bot = 0,
    Paused = 1,
    Human = 2
}

public enum MessageDirection
{
    Inbound = 0,
    Outbound = 1
}

public enum MessageType
{
    Text = 0,
    Image = 1,
    Document = 2,
    Audio = 3,
    Video = 4,
    Template = 5,
    System = 6,
    Other = 7
}

public enum MessageSender
{
    Bot = 0,
    Technician = 1,
    System = 2,
    Customer = 3
}

public enum SupportCaseStatus
{
    Open = 0,
    CollectingInformation = 1,
    ReadyForTechnician = 2,
    HumanHandling = 3,
    Completed = 4,
    Cancelled = 5
}

public enum DeliveryStatus
{
    Pending = 0,
    Sent = 1,
    Delivered = 2,
    Read = 3,
    Failed = 4
}

public enum DimensionUnit
{
    Cm = 0,
    M = 1
}

public enum ConfigConnectionStatus
{
    NotStarted = 0,
    Requested = 1,
    Confirmed = 2,
    Failed = 3
}

public enum MediaAnalysisStatus
{
    NotStarted = 0,
    Pending = 1,
    Completed = 2,
    Failed = 3,
    Skipped = 4
}

public enum AiExtractionType
{
    SlotExtraction = 0,
    IntentDetection = 1,
    VisionLabelAnalysis = 2,
    Other = 3
}
