using System.Text.RegularExpressions;
using LedSupBot.Domain.Entities;

namespace LedSupBot.Application.CaseMemory;

public sealed record CaseMemoryEdit(
    string PrimarySymptom, string? DeviceManufacturer, string? DeviceModel, string? DeviceFamily,
    string? ProblemCategory, string DiagnosticSummary, string FinalResolution,
    string ResolutionSummary, double Confidence = 0.7);

public sealed record CaseMemoryCandidate(Guid Id, string Symptom, string? DeviceModel,
    string? ProblemCategory, string Resolution, double Confidence, double Relevance);

public sealed record CaseMemorySearchResult(bool Attempted, IReadOnlyList<CaseMemoryCandidate> Memories)
{
    public bool Used => Memories.Count > 0;
    public static CaseMemorySearchResult Failed { get; } = new(true, []);
}

public interface IVerifiedCaseMemoryService
{
    Task<VerifiedSupportCaseMemory?> CreateDraftAsync(Guid caseId, CaseMemoryEdit? edit, CancellationToken ct = default);
    Task<VerifiedSupportCaseMemory?> UpdateDraftAsync(Guid id, CaseMemoryEdit edit, CancellationToken ct = default);
    Task<VerifiedSupportCaseMemory?> VerifyAsync(Guid id, string operatorReference, CancellationToken ct = default);
    Task<VerifiedSupportCaseMemory?> SetStatusAsync(Guid id, VerifiedCaseMemoryStatus status, CancellationToken ct = default);
    Task<IReadOnlyList<VerifiedSupportCaseMemory>> ListAsync(VerifiedCaseMemoryStatus? status, string? model, string? category, CancellationToken ct = default);
    Task<CaseMemorySearchResult> SearchAsync(string symptom, string? deviceModel, IReadOnlyCollection<string> completedChecks, CancellationToken ct = default);
}

public static partial class CaseMemoryPrivacy
{
    public static string Sanitize(string? text, int max = 3000)
    {
        var value = text ?? string.Empty;
        value = Phone().Replace(value, "[redacted]");
        value = AnyDesk().Replace(value, "[redacted]");
        value = Secret().Replace(value, "[redacted]");
        value = Path().Replace(value, "[redacted]");
        value = Space().Replace(value, " ").Trim();
        return value.Length <= max ? value : value[..max];
    }

    [GeneratedRegex(@"(?<!\d)(?:\+?90\s*)?0?5\d{2}[\s.()-]*\d{3}[\s.()-]*\d{2}[\s.()-]*\d{2}(?!\d)")]
    private static partial Regex Phone();
    [GeneratedRegex(@"(?i)\banydesk\s*(?:adres(?:i)?|kod(?:u)?|id)?\s*[:#=-]?\s*\d{8,12}\b")]
    private static partial Regex AnyDesk();
    [GeneratedRegex(@"(?i)\b(?:password|parola|api[-_ ]?key|token|bearer|jwt)\s*[:=]\s*[^\s;,]+")]
    private static partial Regex Secret();
    [GeneratedRegex(@"(?i)(?:[a-z]:\\|\\\\)[^\s\""'<>|]+")]
    private static partial Regex Path();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();
}
