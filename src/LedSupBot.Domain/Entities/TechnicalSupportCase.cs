using System.Text.Json;
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

    /// <summary>Operator-facing AI analysis and suggested context.</summary>
    public string? AiOperatorSummary { get; set; }

    /// <summary>Set when AnyDesk readiness operator alert was emitted (dedupe).</summary>
    public DateTimeOffset? AnyDeskAlertSentAt { get; set; }

    /// <summary>Operator dismissed AnyDesk alert without takeover.</summary>
    public DateTimeOffset? AnyDeskAlertDismissedAt { get; set; }

    /// <summary>JSON dictionary of question attempt counts per fact (e.g. {"ScreenDimensions":1,"ReceiverCount":2}).</summary>
    public string? QuestionAttemptsJson { get; set; }

    public ScreenInformation? ScreenInformation { get; set; }

    public ReceiverInformation? ReceiverInformation { get; set; }

    public PlayerType? PlayerType { get; set; }

    public bool? ProcessorHdmiConnected { get; set; }

    public ConfigConnectionInfo? ConfigConnection { get; set; }

    public AnyDeskInformation? AnyDeskInformation { get; set; }

    public ICollection<PanelLabelPhoto> PanelLabelPhotos { get; set; } = new List<PanelLabelPhoto>();

    public int GetQuestionAttempts(string factKey)
    {
        if (string.IsNullOrWhiteSpace(QuestionAttemptsJson))
        {
            return 0;
        }

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, int>>(QuestionAttemptsJson);
            return dict != null && dict.TryGetValue(factKey, out var count) ? count : 0;
        }
        catch
        {
            return 0;
        }
    }

    public void IncrementQuestionAttempts(string factKey)
    {
        Dictionary<string, int> dict;
        try
        {
            dict = !string.IsNullOrWhiteSpace(QuestionAttemptsJson)
                ? JsonSerializer.Deserialize<Dictionary<string, int>>(QuestionAttemptsJson) ?? new(StringComparer.OrdinalIgnoreCase)
                : new(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            dict = new(StringComparer.OrdinalIgnoreCase);
        }

        dict[factKey] = dict.TryGetValue(factKey, out var current) ? current + 1 : 1;
        QuestionAttemptsJson = JsonSerializer.Serialize(dict);
    }

    public void ClearQuestionAttempts(string factKey)
    {
        if (string.IsNullOrWhiteSpace(QuestionAttemptsJson))
        {
            return;
        }

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, int>>(QuestionAttemptsJson) ?? new(StringComparer.OrdinalIgnoreCase);
            if (dict.Remove(factKey))
            {
                QuestionAttemptsJson = JsonSerializer.Serialize(dict);
            }
        }
        catch
        {
            // ignore
        }
    }

    public IReadOnlyDictionary<string, int> GetAllQuestionAttempts()
    {
        if (string.IsNullOrWhiteSpace(QuestionAttemptsJson))
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(QuestionAttemptsJson)
                ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
