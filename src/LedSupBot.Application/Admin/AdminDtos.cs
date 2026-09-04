using LedSupBot.Domain.Enums;

namespace LedSupBot.Application.Admin;

public sealed record ConversationListItemDto(
    Guid Id,
    string CustomerIdentifier,
    string? DisplayName,
    ConversationStatus Status,
    BotMode BotMode,
    ConversationStage CurrentStage,
    string StageLabel,
    string? LastMessagePreview,
    DateTimeOffset? LastCustomerMessageAt,
    DateTimeOffset UpdatedAt,
    int MessageCount,
    bool RequiresAttention);

public sealed record ConversationListResponse(
    IReadOnlyList<ConversationListItemDto> Items,
    int Total);

public sealed record ConversationDetailDto(
    Guid Id,
    ConversationStatus Status,
    BotMode BotMode,
    ConversationStage CurrentStage,
    string StageLabel,
    DateTimeOffset StartedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? LastCustomerMessageAt,
    CustomerSummaryDto Customer,
    CaseDetailDto? Case,
    IReadOnlyList<BotModeAuditDto> RecentAudits,
    string? BotReplyStatusLabel);

public sealed record CustomerSummaryDto(Guid Id, string PhoneNumber, string? DisplayName);

public sealed record CaseDetailDto(
    Guid Id,
    SupportCaseStatus Status,
    string? SummaryMarkdown,
    string? IssueSummary,
    string? AiOperatorSummary,
    ScreenDetailDto? Screen,
    ReceiverDetailDto? Receiver,
    ConfigDetailDto? Config,
    AnyDeskDetailDto? AnyDesk,
    IReadOnlyList<PhotoDetailDto> Photos,
    int CompletedCasesCount = 0,
    PlayerType? PlayerType = null,
    bool? ProcessorHdmiConnected = null);

public sealed record ScreenDetailDto(decimal WidthCm, decimal HeightCm, string DisplayText);

public sealed record ReceiverDetailDto(int Count, string? Model = null, string? DisplayText = null);

public sealed record ConfigDetailDto(ConfigConnectionStatus Status, bool ExampleImageSent, string DisplayText);

public sealed record AnyDeskDetailDto(string MaskedAddress);

public sealed record PhotoDetailDto(
    Guid Id,
    string StorageKey,
    string ContentType,
    long? SizeBytes,
    int SortOrder,
    MediaAnalysisStatus AnalysisStatus,
    string MediaUrl);

public sealed record BotModeAuditDto(BotMode PreviousMode, BotMode NewMode, TakeoverChangeSource Source, DateTimeOffset ChangedAt);

public sealed record MessageDto(
    Guid Id,
    MessageDirection Direction,
    MessageType MessageType,
    string? Content,
    MessageSender SentBy,
    DeliveryStatus DeliveryStatus,
    DateTimeOffset CreatedAt,
    string? StorageKey,
    string? MediaUrl,
    bool TeacherFeedbackEligible = false,
    AiTrainingMessageFeedbackDto? TrainingFeedback = null);

public sealed record AiTrainingMessageFeedbackDto(
    AiTrainingFeedbackStatus FeedbackStatus,
    string? PreferredTechnicianResponse,
    IReadOnlyList<AiTrainingFeedbackReason> ReasonTags,
    AiTrainingEligibility TrainingEligibility,
    bool EligibleForSft,
    bool EligibleForPreference,
    string StatusLabel);

public sealed record MessagesResponse(IReadOnlyList<MessageDto> Messages);

public sealed record SendMessageRequest(string Text);

public sealed record SendMessageResponse(Guid MessageId, string? ExternalMessageId, DeliveryStatus DeliveryStatus);

public sealed record TakeoverResponseDto(bool Changed, BotMode BotMode, ConversationStage Stage, ConversationStatus Status);

public sealed record SystemStatusDto(
    string WhatsAppConnection,
    string Database,
    string OpenAiIntegration,
    bool WorkflowAutoReplyEnabled);

public sealed record CompactRecentMessageDto(string Role, string Content, DateTimeOffset CreatedAt);

public sealed record CompactFocusDto(
    Guid? ConversationId,
    BotMode? BotMode,
    string? BotReplyStatusLabel,
    string? CustomerDisplayName,
    string? IssueSummary,
    string? AiOperatorSummary,
    ConversationStage? CurrentStage,
    string? StageLabel,
    string? ScreenSummary,
    string? ReceiverSummary,
    string PanelStatus,
    string ConfigStatus,
    string AnyDeskStatus,
    int PhotoCount,
    bool ShowAnyDeskAlert,
    IReadOnlyList<CompactRecentMessageDto> RecentMessages,
    int CompletedCasesCount = 0,
    string? ReceiverModel = null,
    int? ReceiverCount = null,
    string? PlayerType = null,
    string? ProcessorHdmiStatus = null);
