using LedSupBot.Domain.Common;
using LedSupBot.Domain.Enums;

namespace LedSupBot.Domain.Entities;

public class TechnicalSupportCase : Entity
{
    public Guid ConversationId { get; set; }

    public Conversation Conversation { get; set; } = null!;

    public SupportCaseStatus Status { get; set; } = SupportCaseStatus.Open;

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Deterministic technician summary markdown (Phase 8/9).</summary>
    public string? SummaryMarkdown { get; set; }

    /// <summary>Short rolling issue summary for operator + AI context (Phase 12).</summary>
    public string? IssueSummary { get; set; }

    /// <summary>Set when AnyDesk readiness operator alert was emitted (dedupe).</summary>
    public DateTimeOffset? AnyDeskAlertSentAt { get; set; }

    /// <summary>Operator dismissed AnyDesk alert without takeover.</summary>
    public DateTimeOffset? AnyDeskAlertDismissedAt { get; set; }

    public ScreenInformation? ScreenInformation { get; set; }

    public ReceiverInformation? ReceiverInformation { get; set; }

    public ConfigConnectionInfo? ConfigConnection { get; set; }

    public AnyDeskInformation? AnyDeskInformation { get; set; }

    public ICollection<PanelLabelPhoto> PanelLabelPhotos { get; set; } = new List<PanelLabelPhoto>();
}
