using System.Text.Json;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Admin;
using LedSupBot.Application.PanelFiles;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LedSupBot.Application.Training;

public interface IAiTrainingService
{
    Task<AiTrainingFeedbackSaveResult> UpsertFeedbackAsync(
        Guid aiResponseMessageId,
        AiTrainingFeedbackRequest request,
        CancellationToken cancellationToken = default);

    Task<AiTrainingListResponse> ListAsync(
        AiTrainingListQuery query,
        CancellationToken cancellationToken = default);

    Task<AiTrainingExampleDetailDto?> GetDetailAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}

public sealed class AiTrainingService : IAiTrainingService
{
    private const int MaxResponseLength = 8000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IApplicationDbContext _db;
    private readonly IAdminEventNotifier _notifier;
    private readonly TrainingCollectionOptions _collectionOptions;
    private readonly IPanelIdentityNormalizer _panelIdentityNormalizer;

    public AiTrainingService(
        IApplicationDbContext db,
        IAdminEventNotifier notifier,
        IOptions<TrainingCollectionOptions> collectionOptions,
        IPanelIdentityNormalizer panelIdentityNormalizer)
    {
        _db = db;
        _notifier = notifier;
        _collectionOptions = collectionOptions.Value;
        _panelIdentityNormalizer = panelIdentityNormalizer;
    }

    public async Task<AiTrainingFeedbackSaveResult> UpsertFeedbackAsync(
        Guid aiResponseMessageId,
        AiTrainingFeedbackRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.FeedbackStatus is not { } feedbackStatus || !Enum.IsDefined(feedbackStatus))
        {
            return AiTrainingFeedbackSaveResult.Failed("invalid_feedback_status");
        }

        if (aiResponseMessageId == Guid.Empty)
        {
            return AiTrainingFeedbackSaveResult.Failed("message_not_training_eligible");
        }

        var preferred = NormalizeOptional(request.PreferredTechnicianResponse);
        var hasStructuredCorrection = !string.IsNullOrWhiteSpace(request.PreferredPixelPitch) ||
            !string.IsNullOrWhiteSpace(request.PreferredPanelCode) || !string.IsNullOrWhiteSpace(request.PreferredSecondaryCode);
        PanelRecognitionTrainingDto? preferredPanelIdentity = null;
        if (hasStructuredCorrection)
        {
            var normalized = _panelIdentityNormalizer.Normalize(
                request.PreferredPixelPitch, request.PreferredPanelCode, request.PreferredSecondaryCode);
            if (normalized.Identity is null) return AiTrainingFeedbackSaveResult.Failed(normalized.Error ?? "panel_identity_is_invalid");
            preferredPanelIdentity = new(normalized.Identity.PixelPitch, normalized.Identity.PanelCode,
                normalized.Identity.SecondaryCode, normalized.Identity.CanonicalKey,
                "OperatorCorrection", "OperatorCorrection", "OperatorCorrection", "Corrected",
                false, false, false, null, null);
        }
        if (preferred?.Length > MaxResponseLength)
        {
            return AiTrainingFeedbackSaveResult.Failed("preferred_response_too_long");
        }

        if (preferred is not null)
        {
            preferred = AiReplySnapshotFactory.SanitizePreferredResponse(preferred, out _);
        }

        if (feedbackStatus == AiTrainingFeedbackStatus.Edited && string.IsNullOrWhiteSpace(preferred) && preferredPanelIdentity is null)
        {
            return AiTrainingFeedbackSaveResult.Failed("preferred_response_required");
        }

        var snapshot = await _db.AiReplyContextSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.AiResponseMessageId == aiResponseMessageId, cancellationToken);

        if (snapshot is null)
        {
            return AiTrainingFeedbackSaveResult.Failed("message_not_training_eligible");
        }

        var eligibleMessageExists = await _db.Messages
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == aiResponseMessageId &&
                     x.ConversationId == snapshot.ConversationId &&
                     x.Direction == MessageDirection.Outbound &&
                     x.SentBy == MessageSender.Bot &&
                     x.MessageType == MessageType.Text &&
                     (x.DeliveryStatus == DeliveryStatus.Sent ||
                      x.DeliveryStatus == DeliveryStatus.Delivered ||
                      x.DeliveryStatus == DeliveryStatus.Read),
                cancellationToken);

        if (!eligibleMessageExists)
        {
            return AiTrainingFeedbackSaveResult.Failed("message_not_training_eligible");
        }

        if ((request.ReasonTags ?? []).Any(tag => !Enum.IsDefined(tag)))
        {
            return AiTrainingFeedbackSaveResult.Failed("invalid_feedback_reason");
        }

        var tags = (request.ReasonTags ?? [])
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        if (feedbackStatus is AiTrainingFeedbackStatus.Approved or AiTrainingFeedbackStatus.Excluded)
        {
            preferred = null;
            preferredPanelIdentity = null;
        }

        var originalPanelIdentity = ParsePanelRecognition(snapshot.PanelIdentityContextSnapshot);
        if (preferredPanelIdentity is not null && originalPanelIdentity is null)
            return AiTrainingFeedbackSaveResult.Failed("message_not_panel_recognition_eligible");

        var technicalClaimRequiresVerification =
            snapshot.TechnicalClaimRequiresVerification ||
            (preferred is not null && AiReplySnapshotFactory.ContainsPotentialTechnicalClaim(preferred));
        var eligibilityOriginal = originalPanelIdentity?.CanonicalKey ?? snapshot.OriginalAiResponse;
        var eligibilityPreferred = preferredPanelIdentity?.CanonicalKey ?? preferred;
        var decision = AiTrainingEligibilityPolicy.Evaluate(
            feedbackStatus,
            eligibilityOriginal,
            eligibilityPreferred,
            originalPanelIdentity is null && technicalClaimRequiresVerification);

        var example = await _db.AiTrainingExamples
            .SingleOrDefaultAsync(x => x.AiResponseMessageId == aiResponseMessageId, cancellationToken);

        var newlyAdded = false;
        if (example is null)
        {
            example = CreateFromSnapshot(snapshot);
            _db.AiTrainingExamples.Add(example);
            newlyAdded = true;
        }

        // Revisions only update the human label. The generation-time snapshot remains immutable.
        ApplyHumanFeedback(example, feedbackStatus, preferred, preferredPanelIdentity, tags, decision,
            technicalClaimRequiresVerification);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (newlyAdded)
        {
            // A simultaneous click may have inserted the unique response row after our read.
            // Detach our failed Added entity, load that logical row, and apply the revision.
            _db.AiTrainingExamples.Remove(example);
            var concurrent = await _db.AiTrainingExamples
                .SingleOrDefaultAsync(x => x.AiResponseMessageId == aiResponseMessageId, cancellationToken);
            if (concurrent is null)
            {
                throw;
            }

            ApplyHumanFeedback(concurrent, feedbackStatus, preferred, preferredPanelIdentity, tags, decision,
                technicalClaimRequiresVerification);
            await _db.SaveChangesAsync(cancellationToken);
            example = concurrent;
        }
        await _notifier.NotifyOperatorStateChangedAsync(example.ConversationId, cancellationToken);

        return new(true, null, ToDetail(example));
    }

    public async Task<AiTrainingListResponse> ListAsync(
        AiTrainingListQuery query,
        CancellationToken cancellationToken = default)
    {
        var all = _db.AiTrainingExamples.AsNoTracking();
        var statusCounts = await all
            .GroupBy(x => x.FeedbackStatus)
            .Select(x => new { Status = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);

        var tuningReady = await all.CountAsync(
            x => x.EligibleForSft &&
                 x.TrainingEligibility != AiTrainingEligibility.NeedsReview &&
                 x.TrainingEligibility != AiTrainingEligibility.Excluded,
            cancellationToken);
        var progress = TrainingCollectionProgressPolicy.Evaluate(tuningReady, _collectionOptions);
        var counts = new AiTrainingCountsDto(
            await all.CountAsync(cancellationToken),
            GetCount(statusCounts, AiTrainingFeedbackStatus.Approved),
            GetCount(statusCounts, AiTrainingFeedbackStatus.Edited),
            GetCount(statusCounts, AiTrainingFeedbackStatus.Rejected),
            await all.CountAsync(x => x.TrainingEligibility == AiTrainingEligibility.NeedsReview, cancellationToken),
            await all.CountAsync(x => x.EligibleForSft, cancellationToken),
            await all.CountAsync(x => x.EligibleForPreference, cancellationToken),
            progress.TuningReady,
            progress.FirstReviewThreshold,
            progress.FirstTuningMin,
            progress.FirstTuningTarget,
            progress.Status);

        IQueryable<AiTrainingExample> filtered = all;
        if (query.Status is { } status)
        {
            filtered = filtered.Where(x => x.FeedbackStatus == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Intent))
        {
            var intent = query.Intent.Trim().ToLowerInvariant();
            filtered = filtered.Where(x => x.CurrentIntent != null && x.CurrentIntent.ToLower() == intent);
        }

        if (query.DateFrom is { } dateFrom)
        {
            filtered = filtered.Where(x => x.CreatedAt >= dateFrom);
        }

        if (query.DateTo is { } dateTo)
        {
            filtered = filtered.Where(x => x.CreatedAt <= dateTo);
        }

        if (query.SourceDecision is { } sourceDecision)
        {
            filtered = filtered.Where(x => x.SourceDecision == sourceDecision);
        }

        var total = await filtered.CountAsync(cancellationToken);
        var rows = await filtered
            .OrderByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(query.Limit, 1, 500))
            .ToListAsync(cancellationToken);

        return new(rows.Select(ToListItem).ToList(), total, counts);
    }

    public async Task<AiTrainingExampleDetailDto?> GetDetailAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var example = await _db.AiTrainingExamples
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return example is null ? null : ToDetail(example);
    }

    private static AiTrainingExample CreateFromSnapshot(AiReplyContextSnapshot snapshot) => new()
    {
        ConversationId = snapshot.ConversationId,
        TechnicalSupportCaseId = snapshot.TechnicalSupportCaseId,
        CustomerMessageId = snapshot.CustomerMessageId,
        AiResponseMessageId = snapshot.AiResponseMessageId,
        TrainingSnapshotSchemaVersion = snapshot.TrainingSnapshotSchemaVersion,
        OriginalCustomerMessage = snapshot.OriginalCustomerMessage,
        OriginalAiResponse = snapshot.OriginalAiResponse,
        CurrentIntent = snapshot.CurrentIntent,
        ResponseAction = snapshot.ResponseAction,
        ReplySource = snapshot.ReplySource,
        GeminiModel = snapshot.GeminiModel,
        PromptPersonaVersion = snapshot.PromptPersonaVersion,
        KnownFactsSnapshot = snapshot.KnownFactsSnapshot,
        MissingFactsSnapshot = snapshot.MissingFactsSnapshot,
        DeferredMissingFactsSnapshot = snapshot.DeferredMissingFactsSnapshot,
        PendingWorkflowFactSnapshot = snapshot.PendingWorkflowFactSnapshot,
        WorkflowStageSnapshot = snapshot.WorkflowStageSnapshot,
        DiagnosticContextSnapshot = snapshot.DiagnosticContextSnapshot,
        DetectedModelsSnapshot = snapshot.DetectedModelsSnapshot,
        ImageContextSummary = snapshot.ImageContextSummary,
        PanelRecognitionSnapshot = snapshot.PanelIdentityContextSnapshot,
        RagAttempted = snapshot.RagAttempted,
        RagUsed = snapshot.RagUsed,
        GroundingLevel = snapshot.GroundingLevel,
        GroundingConfidence = snapshot.GroundingConfidence,
        GroundingConflictDetected = snapshot.GroundingConflictDetected,
        KnowledgeSourceReferences = snapshot.KnowledgeSourceReferences,
        VisionUsed = snapshot.VisionUsed,
        OcrUsed = snapshot.OcrUsed,
        WebSearchAttempted = snapshot.WebSearchAttempted,
        WebSearchUsed = snapshot.WebSearchUsed,
        WebGrounded = snapshot.WebGrounded,
        WebSourceCount = snapshot.WebSourceCount,
        WebSourceReferences = snapshot.WebSourceReferences,
        CaseMemoryAttempted = snapshot.CaseMemoryAttempted,
        CaseMemoryUsed = snapshot.CaseMemoryUsed,
        CaseMemoryCount = snapshot.CaseMemoryCount,
        CaseMemoryReferences = snapshot.CaseMemoryReferences,
        BotModeSnapshot = snapshot.BotModeSnapshot,
        SourceDecision = snapshot.SourceDecision,
        TechnicalClaimRequiresVerification = snapshot.TechnicalClaimRequiresVerification,
        RequiresAnonymization = snapshot.RequiresAnonymization,
    };

    private static void ApplyHumanFeedback(
        AiTrainingExample example,
        AiTrainingFeedbackStatus status,
        string? preferred,
        PanelRecognitionTrainingDto? preferredPanelIdentity,
        IReadOnlyList<AiTrainingFeedbackReason> tags,
        AiTrainingEligibilityDecision decision,
        bool technicalClaimRequiresVerification)
    {
        example.FeedbackStatus = status;
        example.PreferredTechnicianResponse = preferred;
        example.PreferredPanelIdentitySnapshot = preferredPanelIdentity is null ? null : JsonSerializer.Serialize(preferredPanelIdentity, JsonOptions);
        example.FeedbackReasonTags = JsonSerializer.Serialize(tags.Select(x => x.ToString()), JsonOptions);
        example.TrainingEligibility = decision.Eligibility;
        example.EligibleForSft = decision.EligibleForSft;
        example.EligibleForPreference = decision.EligibleForPreference;
        example.TechnicalClaimRequiresVerification = technicalClaimRequiresVerification;
    }

    private static AiTrainingListItemDto ToListItem(AiTrainingExample example) => new(
        example.Id,
        example.ConversationId,
        example.AiResponseMessageId,
        example.FeedbackStatus,
        ParseReasons(example.FeedbackReasonTags),
        example.OriginalCustomerMessage,
        example.OriginalAiResponse,
        example.PreferredTechnicianResponse,
        example.CurrentIntent,
        example.SourceDecision,
        example.TrainingEligibility,
        example.EligibleForSft,
        example.EligibleForPreference,
        example.TechnicalClaimRequiresVerification,
        example.RagUsed,
        example.GroundingLevel,
        example.DiagnosticContextSnapshot,
        example.GeminiModel,
        example.CreatedAt,
        example.UpdatedAt,
        ParsePanelRecognition(example.PanelRecognitionSnapshot),
        ParsePanelRecognition(example.PreferredPanelIdentitySnapshot));

    private static AiTrainingExampleDetailDto ToDetail(AiTrainingExample example) => new(
        example.Id,
        example.ConversationId,
        example.TechnicalSupportCaseId,
        example.CustomerMessageId,
        example.AiResponseMessageId,
        example.TrainingSnapshotSchemaVersion,
        example.FeedbackStatus,
        example.OriginalCustomerMessage,
        example.OriginalAiResponse,
        example.PreferredTechnicianResponse,
        ParseReasons(example.FeedbackReasonTags),
        example.CurrentIntent,
        example.ResponseAction,
        example.ReplySource,
        example.GeminiModel,
        example.PromptPersonaVersion,
        ParseStrings(example.KnownFactsSnapshot),
        ParseStrings(example.MissingFactsSnapshot),
        ParseStrings(example.DeferredMissingFactsSnapshot),
        example.PendingWorkflowFactSnapshot,
        example.WorkflowStageSnapshot,
        example.DiagnosticContextSnapshot,
        ParseStrings(example.DetectedModelsSnapshot),
        example.ImageContextSummary,
        example.RagAttempted,
        example.RagUsed,
        example.GroundingLevel,
        example.GroundingConfidence,
        example.GroundingConflictDetected,
        ParseKnowledgeSources(example.KnowledgeSourceReferences),
        example.VisionUsed,
        example.OcrUsed,
        example.BotModeSnapshot,
        example.SourceDecision,
        example.TrainingEligibility,
        example.EligibleForSft,
        example.EligibleForPreference,
        example.TechnicalClaimRequiresVerification,
        example.RequiresAnonymization,
        example.CreatedAt,
        example.UpdatedAt,
        ParsePanelRecognition(example.PanelRecognitionSnapshot),
        ParsePanelRecognition(example.PreferredPanelIdentitySnapshot));

    private static IReadOnlyList<string> ParseStrings(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<AiTrainingFeedbackReason> ParseReasons(string json) => ParseStrings(json)
        .Select(value => Enum.TryParse<AiTrainingFeedbackReason>(value, true, out var parsed)
            ? (AiTrainingFeedbackReason?)parsed
            : null)
        .Where(value => value.HasValue)
        .Select(value => value!.Value)
        .ToList();

    private static IReadOnlyList<AiTrainingKnowledgeSourceReferenceDto> ParseKnowledgeSources(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AiTrainingKnowledgeSourceReferenceDto[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static PanelRecognitionTrainingDto? ParsePanelRecognition(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            bool Flag(string name) => root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
            Guid? Id(string name) => Guid.TryParse(Text(name), out var value) ? value : null;
            var pitch = Text("pixelPitch"); var code = Text("panelCode"); var secondary = Text("secondaryCode");
            var canonical = Text("canonicalPanelKey") ?? Text("panelIdentityCanonicalKey");
            if (pitch is null && code is null && secondary is null && canonical is null) return null;
            return new(pitch, code, secondary, canonical, Text("pixelPitchSource"), Text("panelCodeSource"),
                Text("secondaryCodeSource"), Text("recognitionStatus") ?? Text("panelIdentityResolutionStatus"),
                Flag("ocrUsed"), Flag("visionUsed"), Flag("conflictDetected"),
                Text("packageResolutionResult") ?? Text("packageResolutionStatus"), Id("exactPackageId") ?? Id("packageId"));
        }
        catch (JsonException) { return null; }
    }

    private static int GetCount(
        IReadOnlyDictionary<AiTrainingFeedbackStatus, int> counts,
        AiTrainingFeedbackStatus status) => counts.TryGetValue(status, out var count) ? count : 0;
}
