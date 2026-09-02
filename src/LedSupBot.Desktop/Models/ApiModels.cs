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

    [JsonPropertyName("whatsAppTestMode")]
    public FoundationTestModeResponse? WhatsAppTestMode { get; set; }
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

    [JsonPropertyName("screen")]
    public ScreenDetailDto? Screen { get; set; }

    [JsonPropertyName("receiver")]
    public ReceiverDetailDto? Receiver { get; set; }

    [JsonPropertyName("config")]
    public ConfigDetailDto? Config { get; set; }

    [JsonPropertyName("anyDesk")]
    public AnyDeskDetailDto? AnyDesk { get; set; }

    [JsonPropertyName("photos")]
    public List<PhotoDetailDto> Photos { get; set; } = [];
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

    [JsonPropertyName("currentStage")]
    public string? CurrentStage { get; set; }

    [JsonPropertyName("stageLabel")]
    public string? StageLabel { get; set; }

    [JsonPropertyName("screenSummary")]
    public string? ScreenSummary { get; set; }

    [JsonPropertyName("receiverSummary")]
    public string? ReceiverSummary { get; set; }

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
