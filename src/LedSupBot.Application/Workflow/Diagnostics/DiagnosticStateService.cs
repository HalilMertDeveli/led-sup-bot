using System.Text.Json;
using System.Text.RegularExpressions;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Media;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedSupBot.Application.Workflow.Diagnostics;

public enum DiagnosticIssueStatus { Active, Resolved, NeedsMoreEvidence, NeedsRemoteSupport, NeedsHumanTechnician, Blocked }
public enum DiagnosticOutcome { Unchanged, Improved, Resolved, Observed, Corrected, Ambiguous }

public sealed class DiagnosticState
{
    public List<DiagnosticIssue> Issues { get; set; } = [];
    public Guid? ActiveIssueId { get; set; }
}
public sealed class DiagnosticIssue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DiagnosticIssueStatus Status { get; set; } = DiagnosticIssueStatus.Active;
    public string PrimarySymptom { get; set; } = "";
    public DiagnosticCheck? PendingCheck { get; set; }
    public List<DiagnosticStep> Steps { get; set; } = [];
    public List<string> Hypotheses { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
}
public sealed record DiagnosticCheck(string SemanticId, string RequestedAction, string? DeviceModel, DateTimeOffset RequestedAt);
public sealed record DiagnosticStep(Guid SourceMessageId, string SemanticCheckId, string RequestedAction, string CustomerResult,
    DiagnosticOutcome Outcome, DateTimeOffset RecordedAt, string? DeviceModel, string? SourceTrace, bool Corrected = false);

public sealed record DiagnosticTurnResult(DiagnosticState State, bool Changed, bool ResumeRequested, bool AmbiguousRecovery, string? DeterministicReply);

public sealed class DiagnosticStateService(IApplicationDbContext db)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DiagnosticTurnResult> ApplyInboundAsync(TechnicalSupportCase supportCase, WorkflowInboundMessage inbound,
        ValidatedImageAnalysis? image, bool workflowPending, CancellationToken ct)
    {
        var tracked = await db.TechnicalSupportCases.SingleAsync(x => x.Id == supportCase.Id, ct);
        var state = Read(tracked.DiagnosticStateJson); var text = inbound.TextContent?.Trim() ?? ""; var changed = false;
        var active = state.Issues.FirstOrDefault(x => x.Id == state.ActiveIssueId && x.Status != DiagnosticIssueStatus.Resolved);

        if (IsExplicitResolution(text) && active is not null)
        {
            CompletePending(active, inbound.MessageId, text, DiagnosticOutcome.Resolved, null); active.Status = DiagnosticIssueStatus.Resolved;
            active.ResolvedAt = active.UpdatedAt = DateTimeOffset.UtcNow; changed = true;
        }
        else if (active?.PendingCheck is not null && IsCheckResult(text))
        {
            CompletePending(active, inbound.MessageId, text, IsUnchanged(text) ? DiagnosticOutcome.Unchanged : DiagnosticOutcome.Observed, null);
            changed = true;
        }
        else if (active?.PendingCheck is not null && image is not null && image.Analysis.VisibleIndicators.Count > 0)
        {
            CompletePending(active, inbound.MessageId, string.Join(", ", image.Analysis.VisibleIndicators), DiagnosticOutcome.Observed, "validated-image-observation");
            changed = true;
        }
        else if (active is not null && IsObservationCorrection(text))
        {
            active.Steps.Add(new(inbound.MessageId, "indicator-observation-correction", "Correct prior observation", text,
                DiagnosticOutcome.Corrected, DateTimeOffset.UtcNow, null, null)); active.UpdatedAt = DateTimeOffset.UtcNow; changed = true;
        }
        else if (IsNewSymptom(text))
        {
            if (active is null || IsDistinctIssue(active.PrimarySymptom, text))
            {
                active = new DiagnosticIssue { PrimarySymptom = NormalizeSymptom(text) };
                state.Issues.Add(active); state.ActiveIssueId = active.Id; changed = true;
            }
        }

        var diagnosticResume = Regex.IsMatch(text, @"(?i)soruna|probleme|arızaya|arizaya.*geri|soruna geri dön|soruna geri don");
        var ambiguous = active is not null && workflowPending && Regex.IsMatch(text, @"(?i)^nerede kalmıştık\??$|^nerede kalmistik\??$");
        string? reply = IsExplicitResolution(text) && active?.Status == DiagnosticIssueStatus.Resolved ? "Tamamdır, sorun çözüldü." : null;
        if (ambiguous) reply = $"Kurulumdaki adıma mı, {active!.PrimarySymptom.ToLowerInvariant()} sorununa mı dönelim?";
        if (changed) await SaveAsync(tracked, state, ct);
        return new(state, changed, diagnosticResume, ambiguous, reply);
    }

    public async Task<bool> ApplyPlannerProposalAsync(Guid caseId, Guid sourceMessageId, ValidatedAiResponsePlan? plan,
        string? detectedModel, CancellationToken ct)
    {
        if (plan is null) return false;
        var tracked = await db.TechnicalSupportCases.SingleAsync(x => x.Id == caseId, ct); var state = Read(tracked.DiagnosticStateJson);
        var active = state.Issues.FirstOrDefault(x => x.Id == state.ActiveIssueId && x.Status != DiagnosticIssueStatus.Resolved);
        if (active is null || plan.Intent != ConversationIntent.LedTroubleshooting) return false;
        if (!string.IsNullOrWhiteSpace(plan.DiagnosticHypothesis) && !active.Hypotheses.Contains(plan.DiagnosticHypothesis, StringComparer.OrdinalIgnoreCase))
            active.Hypotheses.Add(plan.DiagnosticHypothesis[..Math.Min(200, plan.DiagnosticHypothesis.Length)]);
        if (plan.NeedsHuman) active.Status = DiagnosticIssueStatus.NeedsHumanTechnician;
        var action = plan.SuggestedNextAction;
        if (string.IsNullOrWhiteSpace(action)) return false;
        if (Regex.IsMatch(action, @"(?i)anydesk|uzaktan bağlantı|uzaktan baglanti")) active.Status = DiagnosticIssueStatus.NeedsRemoteSupport;
        var id = SemanticId(action); var duplicate = active.Steps.Any(x => x.SemanticCheckId == id && x.Outcome != DiagnosticOutcome.Ambiguous);
        if (!duplicate) active.PendingCheck = new(id, action[..Math.Min(500, action.Length)], detectedModel, DateTimeOffset.UtcNow);
        active.UpdatedAt = DateTimeOffset.UtcNow; await SaveAsync(tracked, state, ct); return duplicate;
    }

    public static DiagnosticState Read(string? json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? new() : JsonSerializer.Deserialize<DiagnosticState>(json, JsonOptions) ?? new(); }
        catch { return new(); }
    }

    public static string BuildContext(DiagnosticState state)
    {
        var active = state.Issues.FirstOrDefault(x => x.Id == state.ActiveIssueId && x.Status != DiagnosticIssueStatus.Resolved);
        if (active is null) return "none";
        var recent = active.Steps.TakeLast(6).Select(x => $"{x.SemanticCheckId}: {x.Outcome} ({x.CustomerResult})");
        return $"ActiveIssue={active.PrimarySymptom}; Status={active.Status}; PendingCheck={(active.PendingCheck is null ? "none" : active.PendingCheck.SemanticId + ": " + active.PendingCheck.RequestedAction)}; PreviouslyTried={string.Join(" | ", recent)}; Hypotheses={string.Join(", ", active.Hypotheses.TakeLast(3))}";
    }

    private async Task SaveAsync(TechnicalSupportCase tracked, DiagnosticState state, CancellationToken ct)
    { tracked.DiagnosticStateJson = JsonSerializer.Serialize(state, JsonOptions); await db.SaveChangesAsync(ct); }
    private static void CompletePending(DiagnosticIssue issue, Guid messageId, string result, DiagnosticOutcome outcome, string? trace)
    { var c = issue.PendingCheck!; issue.Steps.Add(new(messageId, c.SemanticId, c.RequestedAction, result, outcome, DateTimeOffset.UtcNow, c.DeviceModel, trace)); issue.PendingCheck = null; issue.UpdatedAt = DateTimeOffset.UtcNow; }
    private static bool IsExplicitResolution(string t) => Regex.IsMatch(t, @"(?i)\b(düzeldi|çözüldü|cozuldu|şimdi çalışıyor|simdi calisiyor|şimdi geldi|simdi geldi)\b");
    private static bool IsCheckResult(string t) => Regex.IsMatch(t, @"(?i)\b(yaptım|yaptim|olmadı|olmadi|değişmedi|degismedi|hala|başka kabloyla|baska kabloyla|aynı|ayni)\b");
    private static bool IsUnchanged(string t) => Regex.IsMatch(t, @"(?i)\b(aynı|ayni|olmadı|olmadi|değişmedi|degismedi|hala)\b");
    private static bool IsObservationCorrection(string t) => Regex.IsMatch(t, @"(?i)aslında.*(?:yeşil|yesil|kırmızı|kirmizi)|değil.*yanıyor|degil.*yaniyor");
    private static bool IsNewSymptom(string t) => Regex.IsMatch(t, @"(?i)\b(gelmiyor|yanmıyor|yanmiyor|yanlış|yanlis|bozuk|çalışmıyor|calismiyor|gidip geliyor|titriyor|online değil|online degil)\b");
    private static bool IsDistinctIssue(string old, string current) =>
        Regex.IsMatch(current, @"(?i)\b(bir de|ayrıca|ayrica)\b") || (Regex.IsMatch(old, @"(?i)yarı|yari") && Regex.IsMatch(current, @"(?i)renk"));
    private static string NormalizeSymptom(string t) => t[..Math.Min(500, t.Length)];
    public static string SemanticId(string action) => Regex.IsMatch(action, @"(?i)cat6|data kablo") ? "receiver-data-cable-check"
        : Regex.IsMatch(action, @"(?i)online") ? "receiver-online-status-check"
        : Regex.IsMatch(action, @"(?i)ışık|isik|led") ? "receiver-indicator-observation"
        : Regex.IsMatch(action, @"(?i)hdmi") ? "processor-hdmi-input-check"
        : Regex.IsMatch(action, @"(?i)config|usb") ? "config-usb-connection-check"
        : "custom-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(action.ToLowerInvariant())))[..12].ToLowerInvariant();
}
