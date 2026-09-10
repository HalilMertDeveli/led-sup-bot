using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.CaseMemory;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Application.Workflow.Knowledge;
using LedSupBot.Application.Workflow.Web;
using LedSupBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LedSupBot.Application.Workflow.TechnicalAnswers;

public sealed record ApprovedTrainingExampleMatch(
    string CustomerMessage,
    string ExpectedResponse,
    string? Intent,
    double Relevance);

public sealed record ApprovedTrainingSearchResult(
    bool Attempted,
    IReadOnlyList<ApprovedTrainingExampleMatch> Examples)
{
    public bool Used => Examples.Count > 0;
    public static ApprovedTrainingSearchResult NotAttempted { get; } = new(false, []);
}

public sealed record SameCustomerHistoricalCase(
    DateTimeOffset OccurredAt,
    string Summary,
    IReadOnlyList<ContextMessage> Messages,
    double Relevance);

public sealed record SameCustomerMemoryResult(
    bool Attempted,
    IReadOnlyList<SameCustomerHistoricalCase> Cases)
{
    public bool Used => Cases.Count > 0;
    public static SameCustomerMemoryResult NotAttempted { get; } = new(false, []);
}

public sealed record TechnicalAnswerEvidence(
    KnowledgeGroundingResult Spec,
    ApprovedTrainingSearchResult Training,
    SameCustomerMemoryResult SameCustomerMemory,
    CaseMemorySearchResult CaseMemory,
    WebGroundingResult Web,
    InformationSourceRoute WebRoute,
    bool InternalEvidenceSufficient,
    AiRequestBudget RequestBudget)
{
    public string ToOperatorTrace()
    {
        var finalSource = Spec.Level == GroundingLevel.GroundedExact
            ? "InternalSpec"
            : Web.Used ? "Web"
            : Spec.Used ? "InternalSpec"
            : Training.Used ? "ApprovedTrainingStyle"
            : SameCustomerMemory.Used ? "SameCustomerMemory"
            : CaseMemory.Used ? "VerifiedCaseMemory"
            : "GeneralReasoning";
        var webDomains = Web.Sources.Select(x => x.SafeDomain()).Where(x => x is not null).Distinct().Take(4);
        return $"SpecSearch={Spec.Attempted}; SpecResults={Spec.Sources.Count}; " +
               $"TrainingSearch={Training.Attempted}; TrainingResults={Training.Examples.Count}; " +
               $"ChatContext={SameCustomerMemory.Used}; HistoricalCases={SameCustomerMemory.Cases.Count}; " +
               $"CaseMemory={CaseMemory.Used}; WebSearch={Web.Attempted}; WebUsed={Web.Used}; " +
               $"WebDomains={string.Join(',', webDomains)}; EmbeddingCalls={RequestBudget.EmbeddingCalls}; " +
               $"WebCalls={RequestBudget.WebCalls}; GeminiCalls={RequestBudget.GeminiCalls}; FinalSource={finalSource}";
    }
}

public sealed record TechnicalAnswerContextRequest(
    ConversationContext Conversation,
    WorkflowSlotState Slots,
    string Question,
    ConversationIntent Intent,
    string? DiagnosticSymptom = null,
    string? DeviceModel = null,
    IReadOnlyCollection<string>? CompletedChecks = null);

public interface ITechnicalAnswerContextBuilder
{
    Task<TechnicalAnswerEvidence> BuildAsync(TechnicalAnswerContextRequest request, CancellationToken cancellationToken = default);
}

public sealed class TechnicalAnswerContextBuilder(
    TechnicalKnowledgeGroundingService knowledge,
    IApprovedTrainingExampleRetriever training,
    ISameCustomerConversationMemory customerMemory,
    IVerifiedCaseMemoryService caseMemory,
    IWebGroundingService web,
    IOptions<WebGroundingOptions> webOptions,
    ILogger<TechnicalAnswerContextBuilder> logger) : ITechnicalAnswerContextBuilder
{
    public async Task<TechnicalAnswerEvidence> BuildAsync(
        TechnicalAnswerContextRequest request,
        CancellationToken cancellationToken = default)
    {
        var spec = await knowledge.TryGroundAsync(request.Question, request.Slots, request.Conversation, cancellationToken);
        var evidenceEligible = IsEvidenceEligible(request.Intent, spec, request.Question);

        var trainingResult = evidenceEligible
            ? await SearchTrainingSafelyAsync(request, spec, cancellationToken)
            : ApprovedTrainingSearchResult.NotAttempted;
        var customerHistory = evidenceEligible
            ? await SearchCustomerMemorySafelyAsync(request, cancellationToken)
            : SameCustomerMemoryResult.NotAttempted;

        var verifiedMemory = string.IsNullOrWhiteSpace(request.DiagnosticSymptom)
            ? new CaseMemorySearchResult(false, [])
            : await SearchCaseMemorySafelyAsync(request, spec, cancellationToken);

        request.Conversation.RequestBudget.EmbeddingCalls += spec.Attempted ? 1 : 0;

        var sufficient = spec.Level == GroundingLevel.GroundedExact ||
            (spec.Used && spec.Confidence >= 0.68 && !spec.ConflictDetected && !spec.ExactClaimNeedsModel);

        // Web research belongs to the technical-answer lane. A temporal word such as
        // "bugün" in casual conversation must never turn a greeting into a web query.
        // Sufficient internal evidence also wins unless the claim is explicitly current/changeable.
        var asksCurrentProductState = Regex.IsMatch(request.Question,
            @"(?i)\b(firmware|sürüm|surum|versiyon|en son|en yeni|güncel|guncel|hâlâ satılıyor|hala satiliyor)\b");
        var candidateWebRoute = InformationSourceRoutingPolicy.Decide(
            request.Question, request.Intent, spec, webOptions.Value);
        var webRoute = candidateWebRoute != InformationSourceRoute.NoSearch &&
                       (!sufficient || asksCurrentProductState)
            ? candidateWebRoute
            : InformationSourceRoute.NoSearch;
        var webResult = WebGroundingResult.NotAttempted;
        if (webRoute != InformationSourceRoute.NoSearch)
        {
            var publicQuery = InformationSourceRoutingPolicy.BuildPublicQuery(request.Question);
            if (!string.IsNullOrWhiteSpace(publicQuery))
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(webOptions.Value.TimeoutSeconds, 2, 60)));
                    request.Conversation.RequestBudget.WebCalls++;
                    webResult = await web.SearchAsync(publicQuery, timeout.Token);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    logger.LogWarning("Web technical research timed out; continuing with internal evidence");
                    webResult = WebGroundingResult.Failed;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Web technical research failed; continuing with internal evidence");
                    webResult = WebGroundingResult.Failed;
                }
            }
            else webResult = WebGroundingResult.Failed;
        }

        var result = new TechnicalAnswerEvidence(spec, trainingResult, customerHistory, verifiedMemory,
            webResult, webRoute, sufficient, request.Conversation.RequestBudget);
        ApplyToConversation(request.Conversation, result);

        logger.LogInformation(
            "TechnicalAnswerSources intent={Intent} models={Models} specSearch={SpecSearch} specResults={SpecResults} " +
            "trainingSearch={TrainingSearch} trainingResults={TrainingResults} chatContext={ChatContext} " +
            "webSearch={WebSearch} webUsed={WebUsed} embeddingCalls={EmbeddingCalls} webCalls={WebCalls} " +
            "geminiCalls={GeminiCalls} finalSource={FinalSource}",
            request.Intent,
            string.Join(',', spec.DetectedModels),
            spec.Attempted,
            spec.Sources.Count,
            trainingResult.Attempted,
            trainingResult.Examples.Count,
            customerHistory.Used,
            webResult.Attempted,
            webResult.Used,
            request.Conversation.RequestBudget.EmbeddingCalls,
            request.Conversation.RequestBudget.WebCalls,
            request.Conversation.RequestBudget.GeminiCalls,
            result.ToOperatorTrace().Split("FinalSource=")[^1]);
        return result;
    }

    private async Task<ApprovedTrainingSearchResult> SearchTrainingSafelyAsync(
        TechnicalAnswerContextRequest request, KnowledgeGroundingResult spec, CancellationToken cancellationToken)
    {
        try
        {
            return await training.SearchAsync(request.Question, request.Intent, spec.DetectedModels, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Approved training retrieval failed; continuing without training examples");
            return new ApprovedTrainingSearchResult(true, []);
        }
    }

    private async Task<SameCustomerMemoryResult> SearchCustomerMemorySafelyAsync(
        TechnicalAnswerContextRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await customerMemory.SearchAsync(request.Conversation.CustomerId,
                request.Conversation.ConversationId, request.Question, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Same-customer memory retrieval failed; continuing without historical context");
            return new SameCustomerMemoryResult(true, []);
        }
    }

    private async Task<CaseMemorySearchResult> SearchCaseMemorySafelyAsync(
        TechnicalAnswerContextRequest request, KnowledgeGroundingResult spec, CancellationToken cancellationToken)
    {
        try
        {
            return await caseMemory.SearchAsync(request.DiagnosticSymptom!,
                request.DeviceModel ?? spec.DetectedModels.FirstOrDefault(),
                request.CompletedChecks ?? [], cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Verified case-memory retrieval failed; continuing without case memory");
            return CaseMemorySearchResult.Failed;
        }
    }

    private static bool IsEvidenceEligible(ConversationIntent intent, KnowledgeGroundingResult spec, string question) =>
        spec.Attempted || intent is ConversationIntent.LedTechnicalQuestion or ConversationIntent.LedTroubleshooting
            or ConversationIntent.QuestionAboutCurrentStep or ConversationIntent.FlowRecovery ||
        question.Contains("kablo", StringComparison.OrdinalIgnoreCase) ||
        question.Contains("config", StringComparison.OrdinalIgnoreCase) ||
        question.Contains("receiver", StringComparison.OrdinalIgnoreCase) ||
        question.Contains("processor", StringComparison.OrdinalIgnoreCase);

    private static void ApplyToConversation(ConversationContext context, TechnicalAnswerEvidence evidence)
    {
        context.KnowledgeGrounding = evidence.Spec;
        context.TechnicalKnowledgeContext = evidence.Spec.Context;
        context.ApprovedTraining = evidence.Training;
        context.ApprovedTrainingContext = EvidencePromptFormatter.Training(evidence.Training);
        context.SameCustomerMemory = evidence.SameCustomerMemory;
        context.SameCustomerMemoryContext = EvidencePromptFormatter.CustomerMemory(evidence.SameCustomerMemory);
        context.CaseMemory = evidence.CaseMemory;
        context.CaseMemoryContext = EvidencePromptFormatter.CaseMemory(evidence.CaseMemory);
        context.WebGrounding = evidence.Web;
        context.WebEvidenceContext = EvidencePromptFormatter.Web(evidence.Web);
    }
}

public interface IApprovedTrainingExampleRetriever
{
    Task<ApprovedTrainingSearchResult> SearchAsync(string question, ConversationIntent intent,
        IReadOnlyList<string> models, CancellationToken cancellationToken = default);
}

public sealed partial class ApprovedTrainingExampleRetriever(IApplicationDbContext db) : IApprovedTrainingExampleRetriever
{
    private const int CandidateLimit = 120;
    private const int ResultLimit = 5;

    public async Task<ApprovedTrainingSearchResult> SearchAsync(string question, ConversationIntent intent,
        IReadOnlyList<string> models, CancellationToken cancellationToken = default)
    {
        var rows = await db.AiTrainingExamples.AsNoTracking()
            .Where(x => (x.FeedbackStatus == AiTrainingFeedbackStatus.Approved || x.FeedbackStatus == AiTrainingFeedbackStatus.Edited) &&
                        x.EligibleForSft && x.TrainingEligibility != AiTrainingEligibility.NeedsReview &&
                        x.TrainingEligibility != AiTrainingEligibility.Excluded && !x.TechnicalClaimRequiresVerification)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(CandidateLimit)
            .Select(x => new
            {
                x.OriginalCustomerMessage,
                x.OriginalAiResponse,
                x.PreferredTechnicianResponse,
                x.CurrentIntent,
                x.DetectedModelsSnapshot,
            })
            .ToListAsync(cancellationToken);

        var queryTokens = Tokens(question);
        var expectedIntent = intent.ToString();
        var ranked = rows.Select(row =>
            {
                var exampleTokens = Tokens(row.OriginalCustomerMessage);
                var overlap = queryTokens.Count == 0 ? 0 : queryTokens.Intersect(exampleTokens).Count() / (double)queryTokens.Count;
                var intentBoost = string.Equals(row.CurrentIntent, expectedIntent, StringComparison.OrdinalIgnoreCase) ? 0.35 : 0;
                var modelBoost = models.Any(model => ContainsModel(row.DetectedModelsSnapshot, row.OriginalCustomerMessage, model)) ? 0.25 : 0;
                var response = string.IsNullOrWhiteSpace(row.PreferredTechnicianResponse)
                    ? row.OriginalAiResponse
                    : row.PreferredTechnicianResponse!;
                return new
                {
                    Match = new ApprovedTrainingExampleMatch(
                        CaseMemoryPrivacy.Sanitize(row.OriginalCustomerMessage, 500),
                        CaseMemoryPrivacy.Sanitize(response, 800),
                        row.CurrentIntent,
                        Math.Min(1, overlap + intentBoost + modelBoost)),
                    HasContentSignal = overlap > 0 || modelBoost > 0,
                };
            })
            .Where(x => x.HasContentSignal && x.Match.Relevance >= 0.18)
            .OrderByDescending(x => x.Match.Relevance)
            .ThenBy(x => x.Match.CustomerMessage, StringComparer.Ordinal)
            .Take(ResultLimit)
            .Select(x => x.Match)
            .ToList();
        return new(true, ranked);
    }

    private static bool ContainsModel(string json, string message, string model) =>
        json.Contains(model, StringComparison.OrdinalIgnoreCase) || message.Contains(model, StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> Tokens(string value) => TokenRegex().Matches(Normalize(value))
        .Select(x => x.Value).Where(x => x.Length >= 3 && !StopWords.Contains(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string value)
    {
        var decomposed = value.ToLower(new CultureInfo("tr-TR")).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character == 'ı' ? 'i' : character);
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        { "bir", "icin", "ama", "veya", "nedir", "hangi", "misin", "musun", "bana", "bunu", "zirkon", "ledsupport" };
    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)] private static partial Regex TokenRegex();
}

public interface ISameCustomerConversationMemory
{
    Task<SameCustomerMemoryResult> SearchAsync(Guid customerId, Guid currentConversationId, string question,
        CancellationToken cancellationToken = default);
}

public sealed partial class SameCustomerConversationMemory(IApplicationDbContext db) : ISameCustomerConversationMemory
{
    public async Task<SameCustomerMemoryResult> SearchAsync(Guid customerId, Guid currentConversationId, string question,
        CancellationToken cancellationToken = default)
    {
        var cases = await db.TechnicalSupportCases.AsNoTracking()
            .Where(x => x.Conversation.CustomerId == customerId && x.Status == SupportCaseStatus.Completed)
            .OrderByDescending(x => x.CompletedAt ?? x.CreatedAt)
            .Take(8)
            .Select(x => new { x.ConversationId, x.IssueSummary, x.SummaryMarkdown, When = x.CompletedAt ?? x.CreatedAt })
            .ToListAsync(cancellationToken);
        if (cases.Count == 0) return new(true, []);

        var previousConversationIds = cases.Select(x => x.ConversationId).Where(x => x != currentConversationId).Distinct().ToList();
        var messages = previousConversationIds.Count == 0
            ? []
            : await db.Messages.AsNoTracking()
                .Where(x => previousConversationIds.Contains(x.ConversationId) && x.MessageType == MessageType.Text && x.Content != null)
                .OrderByDescending(x => x.CreatedAt)
                .Take(32)
                .Select(x => new { x.ConversationId, x.SentBy, x.Direction, x.Content, x.CreatedAt })
                .ToListAsync(cancellationToken);

        var queryTokens = Tokens(question);
        var ranked = cases.Select(item =>
            {
                var summary = CaseMemoryPrivacy.Sanitize(item.IssueSummary ?? item.SummaryMarkdown ?? "Tamamlanmış LED destek kaydı", 900);
                var history = messages.Where(x => x.ConversationId == item.ConversationId).Take(4)
                    .OrderBy(x => x.CreatedAt)
                    .Select(x => new ContextMessage(x.SentBy == MessageSender.Customer ? "customer" : x.SentBy == MessageSender.Technician ? "technician" : "bot",
                        CaseMemoryPrivacy.Sanitize(x.Content, 350), x.CreatedAt)).ToList();
                var evidenceText = summary + " " + string.Join(' ', history.Select(x => x.Content));
                var evidenceTokens = Tokens(evidenceText);
                var relevance = queryTokens.Count == 0 ? 0 : queryTokens.Intersect(evidenceTokens).Count() / (double)queryTokens.Count;
                return new SameCustomerHistoricalCase(item.When, summary, history, relevance);
            })
            .Where(x => x.Relevance >= 0.12 || cases.Count <= 2)
            .OrderByDescending(x => x.Relevance)
            .ThenByDescending(x => x.OccurredAt)
            .Take(3)
            .ToList();
        return new(true, ranked);
    }

    private static HashSet<string> Tokens(string value) => TokenRegex().Matches(value.ToUpperInvariant())
        .Select(x => x.Value).Where(x => x.Length >= 3).ToHashSet(StringComparer.OrdinalIgnoreCase);
    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)] private static partial Regex TokenRegex();
}

internal static class EvidencePromptFormatter
{
    public static string? Training(ApprovedTrainingSearchResult result) => !result.Used ? null : string.Join("\n",
        result.Examples.Select(x => $"APPROVED BEHAVIOR EXAMPLE: Customer={x.CustomerMessage}; Preferred style response={x.ExpectedResponse}. " +
            "Use only for behavior/style/clarification; never treat it as device specification evidence."));

    public static string? CustomerMemory(SameCustomerMemoryResult result) => !result.Used ? null : string.Join("\n",
        result.Cases.Select(x => $"HISTORICAL SAME-CUSTOMER CASE ({x.OccurredAt:yyyy-MM-dd}): {x.Summary}; " +
            $"bounded messages={string.Join(" | ", x.Messages.Select(m => $"{m.Role}: {m.Content}"))}. " +
            "Historical context is not a current fact unless the customer confirms it for this case."));

    public static string? CaseMemory(CaseMemorySearchResult result) => !result.Used ? null : string.Join("\n",
        result.Memories.Select(x => $"VERIFIED ANONYMIZED SUPPORT LESSON: Similar symptom={x.Symptom}; Device={x.DeviceModel ?? "generic"}; " +
            $"Previous verified experience={x.Resolution}; Confidence={x.Confidence:0.00}. " +
            "Treat as troubleshooting experience, not proof of the current root cause or a product specification."));

    public static string? Web(WebGroundingResult result) => !result.Used ? null :
        $"WEB RESEARCH SUMMARY: {CaseMemoryPrivacy.Sanitize(result.Reply, 1800)}\n" +
        string.Join("\n", result.Sources.Take(6).Select(x =>
            $"WEB SOURCE: title={CaseMemoryPrivacy.Sanitize(x.Title, 180)}; domain={x.SafeDomain() ?? "unknown"}; " +
            $"url={x.SafeUri()}; official={x.IsOfficial}; retrievedAt={x.RetrievedAt:O}"));
}
