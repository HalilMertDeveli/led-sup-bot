using System.Text.Json.Serialization;

namespace LedSupBot.Desktop.Models;

public sealed class AuthStatusResponse
{
    [JsonPropertyName("configured")]
    public bool Configured { get; set; }

    [JsonPropertyName("authenticated")]
    public bool Authenticated { get; set; }
}

public sealed class SystemStatusResponse
{
    [JsonPropertyName("whatsAppConnection")]
    public string WhatsAppConnection { get; set; } = "Disconnected";

    [JsonPropertyName("database")]
    public string Database { get; set; } = "connected";

    [JsonPropertyName("openAiIntegration")]
    public string OpenAiIntegration { get; set; } = "configured";

    [JsonPropertyName("workflowAutoReplyEnabled")]
    public bool WorkflowAutoReplyEnabled { get; set; }
}

public sealed class FoundationStatusResponse
{
    [JsonPropertyName("whatsAppConnection")]
    public string WhatsAppConnection { get; set; } = "Disconnected";

    [JsonPropertyName("inboundPipeline")]
    public string InboundPipeline { get; set; } = "Error";

    [JsonPropertyName("autoReply")]
    public string AutoReply { get; set; } = "Paused";

    [JsonPropertyName("api")]
    public string Api { get; set; } = "Disconnected";

    [JsonPropertyName("whatsAppTestMode")]
    public FoundationTestModeResponse? WhatsAppTestMode { get; set; }
}

public sealed class WhatsAppPairingQrResponse
{
    [JsonPropertyName("available")]
    public bool Available { get; set; }

    [JsonPropertyName("modules")]
    public bool[][] Modules { get; set; } = [];

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Disconnected";
}

public sealed class FoundationTestModeResponse
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("identityConfigured")]
    public bool IdentityConfigured { get; set; }
}

public sealed class ConversationListResponse
{
    [JsonPropertyName("items")]
    public List<ConversationListItemDto> Items { get; set; } = [];

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public sealed class ConversationListItemDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("customerIdentifier")]
    public string CustomerIdentifier { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("botMode")]
    public string BotMode { get; set; } = "";

    [JsonPropertyName("currentStage")]
    public string CurrentStage { get; set; } = "";

    [JsonPropertyName("stageLabel")]
    public string StageLabel { get; set; } = "";

    [JsonPropertyName("lastMessagePreview")]
    public string? LastMessagePreview { get; set; }

    [JsonPropertyName("lastCustomerMessageAt")]
    public DateTimeOffset? LastCustomerMessageAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("messageCount")]
    public int MessageCount { get; set; }

    [JsonPropertyName("requiresAttention")]
    public bool RequiresAttention { get; set; }
}

public sealed class ConversationDetailDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("botMode")]
    public string BotMode { get; set; } = "";

    [JsonPropertyName("currentStage")]
    public string CurrentStage { get; set; } = "";

    [JsonPropertyName("stageLabel")]
    public string StageLabel { get; set; } = "";

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; set; }

    [JsonPropertyName("closedAt")]
    public DateTimeOffset? ClosedAt { get; set; }

    [JsonPropertyName("lastCustomerMessageAt")]
    public DateTimeOffset? LastCustomerMessageAt { get; set; }

    [JsonPropertyName("customer")]
    public CustomerSummaryDto Customer { get; set; } = new();

    [JsonPropertyName("case")]
    public CaseDetailDto? Case { get; set; }

    [JsonPropertyName("recentAudits")]
    public List<BotModeAuditDto> RecentAudits { get; set; } = [];

    [JsonPropertyName("botReplyStatusLabel")]
    public string? BotReplyStatusLabel { get; set; }
}

public sealed class CustomerSummaryDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("phoneNumber")]
    public string PhoneNumber { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
}

public sealed class CaseDetailDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("summaryMarkdown")]
    public string? SummaryMarkdown { get; set; }

    [JsonPropertyName("issueSummary")]
    public string? IssueSummary { get; set; }

    [JsonPropertyName("aiOperatorSummary")]
    public string? AiOperatorSummary { get; set; }

    [JsonPropertyName("completedCasesCount")]
    public int CompletedCasesCount { get; set; }

    [JsonPropertyName("screen")]
    public ScreenDetailDto? Screen { get; set; }

    [JsonPropertyName("receiver")]
    public ReceiverDetailDto? Receiver { get; set; }

    [JsonPropertyName("playerType")]
    public string? PlayerType { get; set; }

    [JsonPropertyName("processorHdmiConnected")]
    public bool? ProcessorHdmiConnected { get; set; }

    [JsonPropertyName("config")]
    public ConfigDetailDto? Config { get; set; }

    [JsonPropertyName("anyDesk")]
    public AnyDeskDetailDto? AnyDesk { get; set; }

    [JsonPropertyName("photos")]
    public List<PhotoDetailDto> Photos { get; set; } = [];
}

public sealed class CaseMemoryDto
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "Draft";
    [JsonPropertyName("primarySymptom")] public string PrimarySymptom { get; set; } = "";
    [JsonPropertyName("deviceManufacturer")] public string? DeviceManufacturer { get; set; }
    [JsonPropertyName("deviceModel")] public string? DeviceModel { get; set; }
    [JsonPropertyName("deviceFamily")] public string? DeviceFamily { get; set; }
    [JsonPropertyName("problemCategory")] public string? ProblemCategory { get; set; }
    [JsonPropertyName("diagnosticSummary")] public string DiagnosticSummary { get; set; } = "";
    [JsonPropertyName("finalResolution")] public string FinalResolution { get; set; } = "";
    [JsonPropertyName("resolutionSummary")] public string ResolutionSummary { get; set; } = "";
    [JsonPropertyName("confidence")] public double Confidence { get; set; } = .7;
}

public sealed class CaseMemoryEditRequest
{
    public string PrimarySymptom { get; set; } = "";
    public string? DeviceManufacturer { get; set; }
    public string? DeviceModel { get; set; }
    public string? DeviceFamily { get; set; }
    public string? ProblemCategory { get; set; }
    public string DiagnosticSummary { get; set; } = "";
    public string FinalResolution { get; set; } = "";
    public string ResolutionSummary { get; set; } = "";
    public double Confidence { get; set; } = .7;
}

public sealed class PanelPackageListResponse
{
    [JsonPropertyName("items")] public List<PanelPackageDto> Items { get; set; } = [];
    [JsonPropertyName("total")] public int Total { get; set; }
}
public sealed class PanelPackageDto
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("pixelPitch")] public string PixelPitch { get; set; } = "";
    [JsonPropertyName("panelCode")] public string PanelCode { get; set; } = "";
    [JsonPropertyName("secondaryCode")] public string SecondaryCode { get; set; } = "";
    [JsonPropertyName("canonicalKey")] public string CanonicalKey { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("manufacturer")] public string? Manufacturer { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("notes")] public string? Notes { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "Draft";
    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("readiness")] public string Readiness { get; set; } = "NOT_DELIVERY_ELIGIBLE";
    [JsonPropertyName("assets")] public List<PanelPackageAssetDto> Assets { get; set; } = [];
    [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; set; }
    [JsonPropertyName("originalFolderName")] public string? OriginalFolderName { get; set; }
    [JsonPropertyName("importedAt")] public DateTimeOffset? ImportedAt { get; set; }
    public int ActiveFileCount => Assets.Count(x => x.Status == "Active");
    public string FirmwareLabel => AssetLabel("FirmwareHex");
    public string ReceiverConfigLabel => AssetLabel("ReceiverConfigRcvbp");
    private string AssetLabel(string type) => Assets.Where(x => x.AssetType == type && x.Status == "Active")
        .OrderByDescending(x => x.Version).Select(x => $"{x.OriginalFileName} · {x.FileSize:N0} B · SHA256 {x.Sha256}").FirstOrDefault() ?? "Eksik";
}
public sealed class PanelPackageAssetDto
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("assetType")] public string AssetType { get; set; } = "";
    [JsonPropertyName("originalFileName")] public string OriginalFileName { get; set; } = "";
    [JsonPropertyName("mimeType")] public string MimeType { get; set; } = "";
    [JsonPropertyName("fileSize")] public long FileSize { get; set; }
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; }
}
public sealed class PanelPackageWriteRequest
{
    public string PixelPitch { get; set; } = ""; public string PanelCode { get; set; } = "";
    public string SecondaryCode { get; set; } = ""; public string? DisplayName { get; set; }
    public string? Manufacturer { get; set; } public string? Description { get; set; }
    public string? Notes { get; set; } public int Version { get; set; } = 1;
}

public sealed class ScreenDetailDto
{
    [JsonPropertyName("widthCm")]
    public decimal WidthCm { get; set; }

    [JsonPropertyName("heightCm")]
    public decimal HeightCm { get; set; }

    [JsonPropertyName("displayText")]
    public string DisplayText { get; set; } = "";
}

public sealed class ReceiverDetailDto
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("displayText")]
    public string? DisplayText { get; set; }
}

public sealed class ConfigDetailDto
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("exampleImageSent")]
    public bool ExampleImageSent { get; set; }

    [JsonPropertyName("displayText")]
    public string DisplayText { get; set; } = "";
}

public sealed class AnyDeskDetailDto
{
    [JsonPropertyName("maskedAddress")]
    public string MaskedAddress { get; set; } = "";
}

public sealed class PhotoDetailDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("storageKey")]
    public string StorageKey { get; set; } = "";

    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = "";

    [JsonPropertyName("sizeBytes")]
    public long? SizeBytes { get; set; }

    [JsonPropertyName("sortOrder")]
    public int SortOrder { get; set; }

    [JsonPropertyName("analysisStatus")]
    public string AnalysisStatus { get; set; } = "";

    [JsonPropertyName("mediaUrl")]
    public string MediaUrl { get; set; } = "";
}

public sealed class BotModeAuditDto
{
    [JsonPropertyName("previousMode")]
    public string PreviousMode { get; set; } = "";

    [JsonPropertyName("newMode")]
    public string NewMode { get; set; } = "";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("changedAt")]
    public DateTimeOffset ChangedAt { get; set; }
}

public sealed class MessagesResponse
{
    [JsonPropertyName("messages")]
    public List<MessageDto> Messages { get; set; } = [];
}

public sealed class MessageDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "";

    [JsonPropertyName("messageType")]
    public string MessageType { get; set; } = "";

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("sentBy")]
    public string SentBy { get; set; } = "";

    [JsonPropertyName("deliveryStatus")]
    public string DeliveryStatus { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("storageKey")]
    public string? StorageKey { get; set; }

    [JsonPropertyName("mediaUrl")]
    public string? MediaUrl { get; set; }

    [JsonPropertyName("teacherFeedbackEligible")]
    public bool TeacherFeedbackEligible { get; set; }

    [JsonPropertyName("trainingFeedback")]
    public TrainingFeedbackSummaryDto? TrainingFeedback { get; set; }

    [JsonPropertyName("panelRecognition")]
    public PanelRecognitionDto? PanelRecognition { get; set; }
}

public sealed class PanelRecognitionDto
{
    [JsonPropertyName("pixelPitch")] public string? PixelPitch { get; set; }
    [JsonPropertyName("panelCode")] public string? PanelCode { get; set; }
    [JsonPropertyName("secondaryCode")] public string? SecondaryCode { get; set; }
    [JsonPropertyName("canonicalKey")] public string? CanonicalKey { get; set; }
    [JsonPropertyName("pixelPitchSource")] public string? PixelPitchSource { get; set; }
    [JsonPropertyName("panelCodeSource")] public string? PanelCodeSource { get; set; }
    [JsonPropertyName("secondaryCodeSource")] public string? SecondaryCodeSource { get; set; }
    [JsonPropertyName("recognitionStatus")] public string? RecognitionStatus { get; set; }
    [JsonPropertyName("ocrUsed")] public bool OcrUsed { get; set; }
    [JsonPropertyName("visionUsed")] public bool VisionUsed { get; set; }
    public string SourceSummary => string.Join(" + ", new[] { PixelPitchSource, PanelCodeSource, SecondaryCodeSource }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
}

public sealed class TrainingFeedbackSummaryDto
{
    [JsonPropertyName("feedbackStatus")]
    public string FeedbackStatus { get; set; } = "";

    [JsonPropertyName("preferredTechnicianResponse")]
    public string? PreferredTechnicianResponse { get; set; }

    [JsonPropertyName("reasonTags")]
    public List<string> ReasonTags { get; set; } = [];

    [JsonPropertyName("trainingEligibility")]
    public string TrainingEligibility { get; set; } = "";

    [JsonPropertyName("eligibleForSft")]
    public bool EligibleForSft { get; set; }

    [JsonPropertyName("eligibleForPreference")]
    public bool EligibleForPreference { get; set; }

    [JsonPropertyName("statusLabel")]
    public string? StatusLabel { get; set; }

    [JsonPropertyName("preferredPanelIdentity")]
    public PanelRecognitionDto? PreferredPanelIdentity { get; set; }
}

public sealed class SaveTrainingFeedbackRequest
{
    [JsonPropertyName("feedbackStatus")]
    public string FeedbackStatus { get; set; } = "";

    [JsonPropertyName("preferredTechnicianResponse")]
    public string? PreferredTechnicianResponse { get; set; }

    [JsonPropertyName("reasonTags")]
    public List<string> ReasonTags { get; set; } = [];

    [JsonPropertyName("preferredPixelPitch")] public string? PreferredPixelPitch { get; set; }
    [JsonPropertyName("preferredPanelCode")] public string? PreferredPanelCode { get; set; }
    [JsonPropertyName("preferredSecondaryCode")] public string? PreferredSecondaryCode { get; set; }
}

public sealed class TrainingExamplesResponse
{
    [JsonPropertyName("counts")]
    public TrainingExampleCountsDto Counts { get; set; } = new();

    [JsonPropertyName("items")]
    public List<TrainingExampleDto> Items { get; set; } = [];
}

public sealed class TrainingExampleCountsDto
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("approved")]
    public int Approved { get; set; }

    [JsonPropertyName("edited")]
    public int Edited { get; set; }

    [JsonPropertyName("rejected")]
    public int Rejected { get; set; }

    [JsonPropertyName("needsReview")]
    public int NeedsReview { get; set; }

    [JsonPropertyName("eligibleForSft")]
    public int EligibleForSft { get; set; }

    [JsonPropertyName("eligibleForPreference")]
    public int EligibleForPreference { get; set; }

    [JsonPropertyName("tuningReady")]
    public int TuningReady { get; set; }

    [JsonPropertyName("firstReviewThreshold")]
    public int FirstReviewThreshold { get; set; } = 200;

    [JsonPropertyName("firstTuningMin")]
    public int FirstTuningMin { get; set; } = 300;

    [JsonPropertyName("firstTuningTarget")]
    public int FirstTuningTarget { get; set; } = 500;

    [JsonPropertyName("collectionStatus")]
    public string CollectionStatus { get; set; } = "COLLECTING";

    public string ReviewProgressText => $"{TuningReady} / {FirstReviewThreshold}";
    public string TuningRangeText => $"{FirstTuningMin}–{FirstTuningTarget}";
}

public sealed class TrainingExampleDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("aiResponseMessageId")]
    public Guid AiResponseMessageId { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("feedbackStatus")]
    public string FeedbackStatus { get; set; } = "";

    [JsonPropertyName("trainingEligibility")]
    public string TrainingEligibility { get; set; } = "";

    [JsonPropertyName("currentIntent")]
    public string? CurrentIntent { get; set; }

    [JsonPropertyName("sourceDecision")]
    public string? SourceDecision { get; set; }

    [JsonPropertyName("originalCustomerMessage")]
    public string? OriginalCustomerMessage { get; set; }

    [JsonPropertyName("originalAiResponse")]
    public string OriginalAiResponse { get; set; } = "";

    [JsonPropertyName("preferredTechnicianResponse")]
    public string? PreferredTechnicianResponse { get; set; }

    [JsonPropertyName("reasonTags")]
    public List<string> ReasonTags { get; set; } = [];

    [JsonPropertyName("ragUsed")]
    public bool RagUsed { get; set; }

    [JsonPropertyName("groundingLevel")]
    public string? GroundingLevel { get; set; }

    [JsonPropertyName("diagnosticContextSummary")]
    public string? DiagnosticContextSummary { get; set; }

    [JsonPropertyName("panelRecognition")] public PanelRecognitionDto? PanelRecognition { get; set; }
    [JsonPropertyName("preferredPanelIdentity")] public PanelRecognitionDto? PreferredPanelIdentity { get; set; }

    [JsonPropertyName("geminiModel")]
    public string? GeminiModel { get; set; }

    [JsonIgnore]
    public string DisplayTitle => $"{FeedbackStatus} · {CurrentIntent ?? "Intent yok"}";

    [JsonIgnore]
    public string DisplaySubtitle => $"{CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm} · {SourceDecision ?? "Kaynak yok"}";

    [JsonIgnore]
    public string ReasonTagsText => ReasonTags.Count == 0 ? "—" : string.Join(", ", ReasonTags);

    [JsonIgnore]
    public string RagUsedText => RagUsed ? "Evet" : "Hayır";
}

public sealed class SendMessageRequest
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}

public sealed class SendMessageResponse
{
    [JsonPropertyName("messageId")]
    public Guid MessageId { get; set; }

    [JsonPropertyName("externalMessageId")]
    public string? ExternalMessageId { get; set; }

    [JsonPropertyName("deliveryStatus")]
    public string DeliveryStatus { get; set; } = "";
}

public sealed class CompactFocusResponse
{
    [JsonPropertyName("conversationId")]
    public Guid? ConversationId { get; set; }

    [JsonPropertyName("botMode")]
    public string? BotMode { get; set; }

    [JsonPropertyName("botReplyStatusLabel")]
    public string? BotReplyStatusLabel { get; set; }

    [JsonPropertyName("customerDisplayName")]
    public string? CustomerDisplayName { get; set; }

    [JsonPropertyName("issueSummary")]
    public string? IssueSummary { get; set; }

    [JsonPropertyName("aiOperatorSummary")]
    public string? AiOperatorSummary { get; set; }

    [JsonPropertyName("currentStage")]
    public string? CurrentStage { get; set; }

    [JsonPropertyName("stageLabel")]
    public string? StageLabel { get; set; }

    [JsonPropertyName("screenSummary")]
    public string? ScreenSummary { get; set; }

    [JsonPropertyName("receiverSummary")]
    public string? ReceiverSummary { get; set; }

    [JsonPropertyName("receiverModel")]
    public string? ReceiverModel { get; set; }

    [JsonPropertyName("receiverCount")]
    public int? ReceiverCount { get; set; }

    [JsonPropertyName("playerType")]
    public string? PlayerType { get; set; }

    [JsonPropertyName("processorHdmiStatus")]
    public string? ProcessorHdmiStatus { get; set; }

    [JsonPropertyName("panelStatus")]
    public string PanelStatus { get; set; } = "—";

    [JsonPropertyName("configStatus")]
    public string ConfigStatus { get; set; } = "—";

    [JsonPropertyName("anyDeskStatus")]
    public string AnyDeskStatus { get; set; } = "—";

    [JsonPropertyName("photoCount")]
    public int PhotoCount { get; set; }

    [JsonPropertyName("showAnyDeskAlert")]
    public bool ShowAnyDeskAlert { get; set; }

    [JsonPropertyName("completedCasesCount")]
    public int CompletedCasesCount { get; set; }

    [JsonPropertyName("recentMessages")]
    public List<CompactRecentMessage> RecentMessages { get; set; } = [];
}

public sealed class CompactRecentMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TakeoverResponse
{
    [JsonPropertyName("changed")]
    public bool Changed { get; set; }

    [JsonPropertyName("botMode")]
    public string BotMode { get; set; } = "";
}

public sealed class ConversationEventPayload
{
    [JsonPropertyName("conversationId")]
    public Guid ConversationId { get; set; }
}

public sealed class MessageEventPayload
{
    [JsonPropertyName("conversationId")]
    public Guid ConversationId { get; set; }

    [JsonPropertyName("messageId")]
    public Guid MessageId { get; set; }
}

public sealed class WhatsAppStatusEventPayload
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public sealed class OperatorStateEventPayload
{
    [JsonPropertyName("conversationId")]
    public Guid? ConversationId { get; set; }

    [JsonPropertyName("messageId")]
    public Guid? MessageId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public sealed class PanelPackagesChangedEventPayload
{
    public Guid PackageId { get; set; }
    public string? Change { get; set; }
}

public sealed class AnyDeskAlertEvent
{
    [JsonPropertyName("conversationId")]
    public Guid ConversationId { get; set; }

    [JsonPropertyName("customerDisplayName")]
    public string? CustomerDisplayName { get; set; }

    [JsonPropertyName("issueSummary")]
    public string? IssueSummary { get; set; }

    [JsonPropertyName("screenSummary")]
    public string? ScreenSummary { get; set; }

    [JsonPropertyName("receiverSummary")]
    public string? ReceiverSummary { get; set; }

    [JsonPropertyName("panelStatus")]
    public string? PanelStatus { get; set; }

    [JsonPropertyName("configStatus")]
    public string? ConfigStatus { get; set; }

    [JsonPropertyName("anyDeskStatus")]
    public string? AnyDeskStatus { get; set; }
}
