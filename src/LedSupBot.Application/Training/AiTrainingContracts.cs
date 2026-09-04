using LedSupBot.Domain.Enums;

namespace LedSupBot.Application.Training;

public sealed record AiTrainingFeedbackRequest(
    AiTrainingFeedbackStatus? FeedbackStatus,
    string? PreferredTechnicianResponse = null,
    IReadOnlyList<AiTrainingFeedbackReason>? ReasonTags = null);

public sealed record AiTrainingFeedbackSaveResult(
    bool Success,
    string? Error,
    AiTrainingExampleDetailDto? Example)
{
    public static AiTrainingFeedbackSaveResult Failed(string error) => new(false, error, null);
}

public sealed record AiTrainingListQuery(
    AiTrainingFeedbackStatus? Status = null,
    string? Intent = null,
    DateTimeOffset? DateFrom = null,
    DateTimeOffset? DateTo = null,
    AiTrainingSourceDecision? SourceDecision = null,
    int Limit = 200);

public sealed record AiTrainingCountsDto(
    int Total,
    int Approved,
    int Edited,
    int Rejected,
    int NeedsReview,
    int EligibleForSft,
    int EligibleForPreference,
    int TuningReady,
    int FirstReviewThreshold,
    int FirstTuningMin,
    int FirstTuningTarget,
    string CollectionStatus);

public sealed record AiTrainingListItemDto(
    Guid Id,
    Guid ConversationId,
    Guid AiResponseMessageId,
    AiTrainingFeedbackStatus FeedbackStatus,
    IReadOnlyList<AiTrainingFeedbackReason> ReasonTags,
    string OriginalCustomerMessage,
    string OriginalAiResponse,
    string? PreferredTechnicianResponse,
    string? CurrentIntent,
    AiTrainingSourceDecision SourceDecision,
    AiTrainingEligibility TrainingEligibility,
    bool EligibleForSft,
    bool EligibleForPreference,
    bool TechnicalClaimRequiresVerification,
    bool RagUsed,
    string? GroundingLevel,
    string? DiagnosticContextSummary,
    string? GeminiModel,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AiTrainingListResponse(
    IReadOnlyList<AiTrainingListItemDto> Items,
    int Total,
    AiTrainingCountsDto Counts);

public sealed record AiTrainingKnowledgeSourceReferenceDto(
    string Document,
    string? DocumentVersion,
    int PageStart,
    int PageEnd,
    string? Section,
    string? GroundingLevel);

public sealed record AiTrainingExampleDetailDto(
    Guid Id,
    Guid ConversationId,
    Guid? TechnicalSupportCaseId,
    Guid CustomerMessageId,
    Guid AiResponseMessageId,
    int TrainingSnapshotSchemaVersion,
    AiTrainingFeedbackStatus FeedbackStatus,
    string OriginalCustomerMessage,
    string OriginalAiResponse,
    string? PreferredTechnicianResponse,
    IReadOnlyList<AiTrainingFeedbackReason> ReasonTags,
    string? CurrentIntent,
    string? ResponseAction,
    string? ReplySource,
    string? GeminiModel,
    string? PromptPersonaVersion,
    IReadOnlyList<string> KnownFactsSnapshot,
    IReadOnlyList<string> MissingFactsSnapshot,
    IReadOnlyList<string> DeferredMissingFactsSnapshot,
    string? PendingWorkflowFactSnapshot,
    string? WorkflowStageSnapshot,
    string? DiagnosticContextSummary,
    IReadOnlyList<string> DetectedModelsSnapshot,
    string? ImageContextSummary,
    bool RagAttempted,
    bool RagUsed,
    string? GroundingLevel,
    double? GroundingConfidence,
    bool GroundingConflictDetected,
    IReadOnlyList<AiTrainingKnowledgeSourceReferenceDto> KnowledgeSourceReferences,
    bool VisionUsed,
    bool OcrUsed,
    string? BotModeSnapshot,
    AiTrainingSourceDecision SourceDecision,
    AiTrainingEligibility TrainingEligibility,
    bool EligibleForSft,
    bool EligibleForPreference,
    bool TechnicalClaimRequiresVerification,
    bool RequiresAnonymization,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AiTrainingEligibilityDecision(
    AiTrainingEligibility Eligibility,
    bool EligibleForSft,
    bool EligibleForPreference);

public static class AiTrainingEligibilityPolicy
{
    public static AiTrainingEligibilityDecision Evaluate(
        AiTrainingFeedbackStatus status,
        string originalAiResponse,
        string? preferredTechnicianResponse,
        bool technicalClaimRequiresVerification)
    {
        if (status == AiTrainingFeedbackStatus.Excluded)
        {
            return new(AiTrainingEligibility.Excluded, false, false);
        }

        var hasPreferred = !string.IsNullOrWhiteSpace(preferredTechnicianResponse);
        var preferencePair = hasPreferred &&
            !string.Equals(originalAiResponse.Trim(), preferredTechnicianResponse!.Trim(), StringComparison.Ordinal);

        if (status == AiTrainingFeedbackStatus.Rejected && !hasPreferred)
        {
            return new(AiTrainingEligibility.Excluded, false, false);
        }

        if (technicalClaimRequiresVerification)
        {
            return new(AiTrainingEligibility.NeedsReview, false, false);
        }

        return status switch
        {
            AiTrainingFeedbackStatus.Approved =>
                new(AiTrainingEligibility.EligibleForSft, true, false),
            AiTrainingFeedbackStatus.Edited => preferencePair
                ? new(AiTrainingEligibility.EligibleForPreference, true, true)
                : new(AiTrainingEligibility.EligibleForSft, true, false),
            AiTrainingFeedbackStatus.Rejected => preferencePair
                ? new(AiTrainingEligibility.EligibleForPreference, true, true)
                : new(AiTrainingEligibility.Excluded, false, false),
            _ => new(AiTrainingEligibility.Excluded, false, false),
        };
    }
}
