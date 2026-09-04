using LedSupBot.Domain.Common;
using LedSupBot.Domain.Enums;

namespace LedSupBot.Domain.Entities;

/// <summary>A human-labelled, provider-neutral future training candidate.</summary>
public sealed class AiTrainingExample : Entity
{
    public Guid ConversationId { get; set; }

    public Guid? TechnicalSupportCaseId { get; set; }

    public Guid CustomerMessageId { get; set; }

    public Guid AiResponseMessageId { get; set; }

    public int TrainingSnapshotSchemaVersion { get; set; } = AiReplyContextSnapshot.CurrentSchemaVersion;

    public AiTrainingFeedbackStatus FeedbackStatus { get; set; }

    public string OriginalCustomerMessage { get; set; } = string.Empty;

    public string OriginalAiResponse { get; set; } = string.Empty;

    public string? PreferredTechnicianResponse { get; set; }

    /// <summary>Bounded JSON array of AiTrainingFeedbackReason names.</summary>
    public string FeedbackReasonTags { get; set; } = "[]";

    public string? CurrentIntent { get; set; }

    public string? ResponseAction { get; set; }

    public string? ReplySource { get; set; }

    public string? GeminiModel { get; set; }

    public string? PromptPersonaVersion { get; set; }

    public string KnownFactsSnapshot { get; set; } = "[]";

    public string MissingFactsSnapshot { get; set; } = "[]";

    public string DeferredMissingFactsSnapshot { get; set; } = "[]";

    public string? PendingWorkflowFactSnapshot { get; set; }

    public string? WorkflowStageSnapshot { get; set; }

    public string? DiagnosticContextSnapshot { get; set; }

    public string DetectedModelsSnapshot { get; set; } = "[]";

    public string? ImageContextSummary { get; set; }

    public bool RagAttempted { get; set; }

    public bool RagUsed { get; set; }

    public string? GroundingLevel { get; set; }

    public double? GroundingConfidence { get; set; }

    public bool GroundingConflictDetected { get; set; }

    public string KnowledgeSourceReferences { get; set; } = "[]";

    public bool VisionUsed { get; set; }

    public bool OcrUsed { get; set; }

    public string? BotModeSnapshot { get; set; }

    public AiTrainingSourceDecision SourceDecision { get; set; }

    public AiTrainingEligibility TrainingEligibility { get; set; }

    public bool EligibleForSft { get; set; }

    public bool EligibleForPreference { get; set; }

    public bool TechnicalClaimRequiresVerification { get; set; }

    public bool RequiresAnonymization { get; set; } = true;
}
