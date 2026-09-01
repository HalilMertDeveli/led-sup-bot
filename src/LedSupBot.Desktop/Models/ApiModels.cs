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
