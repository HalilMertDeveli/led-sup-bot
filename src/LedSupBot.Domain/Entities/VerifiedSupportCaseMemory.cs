using LedSupBot.Domain.Common;
using Pgvector;
using System.Text.Json.Serialization;

namespace LedSupBot.Domain.Entities;

public enum VerifiedCaseMemoryStatus { Draft = 0, Verified = 1, Rejected = 2, Archived = 3 }

public sealed class VerifiedSupportCaseMemory : Entity
{
    public Guid TechnicalSupportCaseId { get; set; }
    public Guid ConversationId { get; set; }
    public VerifiedCaseMemoryStatus Status { get; set; } = VerifiedCaseMemoryStatus.Draft;
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? VerifiedBy { get; set; }
    public string PrimarySymptom { get; set; } = string.Empty;
    public string NormalizedSymptom { get; set; } = string.Empty;
    public string? DeviceManufacturer { get; set; }
    public string? DeviceModel { get; set; }
    public string? DeviceFamily { get; set; }
    public string? ProblemCategory { get; set; }
    public string DiagnosticSummary { get; set; } = string.Empty;
    public string CompletedChecks { get; set; } = "[]";
    public string FailedChecks { get; set; } = "[]";
    public string SuccessfulChecks { get; set; } = "[]";
    public string FinalResolution { get; set; } = string.Empty;
    public string ResolutionSummary { get; set; } = string.Empty;
    public string RelevantWorkflowFacts { get; set; } = "[]";
    public string RelevantDiagnosticStateSnapshot { get; set; } = "{}";
    public string SourceMessageIds { get; set; } = "[]";
    public double Confidence { get; set; }
    public int MemorySchemaVersion { get; set; } = 1;
    [JsonIgnore] public string RetrievalText { get; set; } = string.Empty;
    [JsonIgnore] public Vector? Embedding { get; set; }
}
