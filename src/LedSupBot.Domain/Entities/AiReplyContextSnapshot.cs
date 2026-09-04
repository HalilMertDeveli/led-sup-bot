using LedSupBot.Domain.Common;
using LedSupBot.Domain.Enums;

namespace LedSupBot.Domain.Entities;

/// <summary>
/// Immutable, provider-neutral context captured when an AI reply is generated.
/// This is diagnostic staging data, not an operator-approved training example.
/// </summary>
public sealed class AiReplyContextSnapshot : Entity
{
    public const int CurrentSchemaVersion = 3;

    public Guid ConversationId { get; set; }

    public Guid? TechnicalSupportCaseId { get; set; }

    public Guid CustomerMessageId { get; set; }

    public Guid AiResponseMessageId { get; set; }

    public int TrainingSnapshotSchemaVersion { get; set; } = CurrentSchemaVersion;

    public string OriginalCustomerMessage { get; set; } = string.Empty;

    public string OriginalAiResponse { get; set; } = string.Empty;

    public string? CurrentIntent { get; set; }

    public string? ResponseAction { get; set; }

    public string? ReplySource { get; set; }

    public string? GeminiModel { get; set; }

    public string? PromptPersonaVersion { get; set; }

    /// <summary>Bounded JSON string array.</summary>
    public string KnownFactsSnapshot { get; set; } = "[]";

    /// <summary>Bounded JSON string array.</summary>
    public string MissingFactsSnapshot { get; set; } = "[]";

    /// <summary>Bounded JSON string array.</summary>
    public string DeferredMissingFactsSnapshot { get; set; } = "[]";

    public string? PendingWorkflowFactSnapshot { get; set; }

    public string? WorkflowStageSnapshot { get; set; }

    public string? DiagnosticContextSnapshot { get; set; }

    /// <summary>Bounded JSON string array.</summary>
    public string DetectedModelsSnapshot { get; set; } = "[]";

    public string? ImageContextSummary { get; set; }

    public bool RagAttempted { get; set; }

    public bool RagUsed { get; set; }

    public string? GroundingLevel { get; set; }

    public double? GroundingConfidence { get; set; }

    public bool GroundingConflictDetected { get; set; }

    /// <summary>Bounded JSON evidence references; never complete PDF chunks.</summary>
    public string KnowledgeSourceReferences { get; set; } = "[]";

    public bool VisionUsed { get; set; }

    public bool OcrUsed { get; set; }

    public bool WebSearchAttempted { get; set; }
    public bool WebSearchUsed { get; set; }
    public bool WebGrounded { get; set; }
    public int WebSourceCount { get; set; }
    public string WebSourceReferences { get; set; } = "[]";
    public bool CaseMemoryAttempted { get; set; }
    public bool CaseMemoryUsed { get; set; }
    public int CaseMemoryCount { get; set; }
    public string CaseMemoryReferences { get; set; } = "[]";

    public string? BotModeSnapshot { get; set; }

    public AiTrainingSourceDecision SourceDecision { get; set; }

    public bool TechnicalClaimRequiresVerification { get; set; }

    /// <summary>True when export must run the T2 anonymization pipeline.</summary>
    public bool RequiresAnonymization { get; set; } = true;
}
