using System.Text.Json;
using System.Text.RegularExpressions;
using LedSupBot.Application.Media;
using LedSupBot.Application.Workflow;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Application.Workflow.Knowledge;
using LedSupBot.Application.Workflow.Web;
using LedSupBot.Domain.Enums;

namespace LedSupBot.Application.Training;

/// <summary>
/// Provider-neutral, bounded provenance captured at the instant an AI reply is persisted.
/// This is an unlabeled audit snapshot, not an approved training example.
/// </summary>
public sealed record AiReplySnapshotDraft(
    Guid TechnicalSupportCaseId,
    Guid CustomerMessageId,
    string OriginalCustomerMessage,
    string OriginalAiResponse,
    string CurrentIntent,
    string ResponseAction,
    string ReplySource,
    string? GeminiModel,
    string PromptVersion,
    string KnownFactsSnapshotJson,
    string MissingFactsSnapshotJson,
    string DeferredMissingFactsSnapshotJson,
    string PendingWorkflowFactSnapshot,
    string WorkflowStageSnapshot,
    string DiagnosticContextSnapshot,
    string DetectedModelsSnapshotJson,
    string? ImageContextSummary,
    bool RagAttempted,
    bool RagUsed,
    string GroundingLevel,
    double GroundingConfidence,
    bool GroundingConflictDetected,
    string KnowledgeSourceReferencesJson,
    bool VisionUsed,
    bool OcrUsed,
    string BotModeSnapshot,
    AiTrainingSourceDecision SourceDecision,
    bool TechnicalClaimRequiresVerification,
    bool RequiresAnonymization,
    bool ContainsRedactions,
    int TrainingSnapshotSchemaVersion = 4,
    bool WebSearchAttempted = false,
    bool WebSearchUsed = false,
    bool WebGrounded = false,
    int WebSourceCount = 0,
    string WebSourceReferencesJson = "[]",
    bool CaseMemoryAttempted = false,
    bool CaseMemoryUsed = false,
    int CaseMemoryCount = 0,
    string CaseMemoryReferencesJson = "[]",
    string? PanelIdentityContextJson = null);

public sealed record AiKnowledgeSourceSnapshot(
    string Document,
    string DocumentVersion,
    string Model,
    int PageStart,
    int PageEnd,
    string Section,
    string GroundingLevel,
    double Score);

public static partial class AiReplySnapshotFactory
{
    public const int SchemaVersion = 4;
    public const string ResponsePromptVersion = "conversation-planner-v1";
    private const int MaxFactCount = 24;
    private const int MaxFactLength = 300;
    private const int MaxDiagnosticLength = 3000;
    private const int MaxCustomerMessageLength = 4000;
    private const int MaxAiResponseLength = 4000;
    private const int MaxPreferredResponseLength = 8000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> BackendMatchStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "adet", "alındı", "bağlantısı", "bilgisi", "başarıyla", "cm", "ekran", "kayıt",
        "modeli", "onaylandı", "önceki", "receiver", "sorun", "teknik", "türü",
    };

    public static AiReplySnapshotDraft Create(
        Guid technicalSupportCaseId,
        Guid customerMessageId,
        string? customerMessage,
        string aiResponse,
        ConversationContext context,
        ConversationStage workflowStage,
        PendingWorkflowFact pendingWorkflowFact,
        ConversationResponseAction responseAction,
        LedSupBot.Application.Abstractions.ConversationIntent intent,
        KnowledgeGroundingResult grounding,
        ValidatedImageAnalysis? image,
        string? diagnosticContext,
        string? geminiModel,
        AiTrainingSourceDecision? sourceDecisionOverride = null,
        string? replySourceOverride = null,
        WebGroundingResult? web = null,
        string? panelIdentityContextJson = null)
    {
        var safeCustomer = Sanitize(customerMessage, MaxCustomerMessageLength, out var customerRedacted);
        var safeResponse = Sanitize(aiResponse, MaxAiResponseLength, out var responseRedacted);
        var safeDiagnostic = Sanitize(diagnosticContext, MaxDiagnosticLength, out var diagnosticRedacted);
        var visionUsed = image?.Analysis.VisionSucceeded == true;
        var ocrUsed = image?.Analysis.OcrSucceeded == true;
        var diagnosticUsed = UsesDiagnosticState(safeCustomer, safeResponse, safeDiagnostic);
        var backendStateUsed = UsesBackendState(safeCustomer, safeResponse, context.KnownFacts);
        var sourceDecision = DeriveSourceDecision(grounding.Used, visionUsed || ocrUsed, diagnosticUsed, backendStateUsed);
        var sources = grounding.Sources
            .Take(6)
            .Select(source => new AiKnowledgeSourceSnapshot(
                SafeDocumentReference(source.Document),
                Sanitize(source.DocumentVersion, 80, out _),
                Sanitize(source.Model, 100, out _),
                source.PageStart,
                source.PageEnd,
                Sanitize(source.SectionTitle, 240, out _),
                grounding.Level.ToString(),
                Math.Round(source.Score, 4)))
            .ToList();

        return new AiReplySnapshotDraft(
            technicalSupportCaseId,
            customerMessageId,
            safeCustomer,
            safeResponse,
            intent.ToString(),
            responseAction.ToString(),
            Bounded(replySourceOverride ?? BuildReplySource(grounding.Used, visionUsed, ocrUsed, diagnosticUsed), 128),
            BoundedOptional(geminiModel, 128),
            ResponsePromptVersion,
            SerializeStrings(context.KnownFacts),
            SerializeStrings(context.MissingFacts),
            SerializeStrings(context.DeferredMissingFacts),
            pendingWorkflowFact.ToString(),
            workflowStage.ToString(),
            safeDiagnostic,
            SerializeStrings(grounding.DetectedModels),
            BuildImageSummary(image),
            grounding.Attempted,
            grounding.Used,
            grounding.Level.ToString(),
            Math.Round(grounding.Confidence, 4),
            grounding.ConflictDetected,
            JsonSerializer.Serialize(sources, JsonOptions),
            visionUsed,
            ocrUsed,
            context.BotMode.ToString(),
            sourceDecisionOverride ?? sourceDecision,
            RequiresTechnicalVerification(safeResponse, intent, grounding),
            RequiresAnonymization: true,
            ContainsRedactions: customerRedacted || responseRedacted || diagnosticRedacted,
            SchemaVersion,
            web?.Attempted == true,
            web?.Used == true,
            web?.Grounded == true,
            Math.Min(web?.Sources.Count ?? 0, 6),
            JsonSerializer.Serialize((web?.Sources ?? []).Take(6).Select(x => new
            {
                title = Sanitize(x.Title, 160, out _),
                uri = SafeWebReference(x.Uri),
            }), JsonOptions),
            context.CaseMemory?.Attempted == true,
            context.CaseMemory?.Used == true,
            Math.Min(context.CaseMemory?.Memories.Count ?? 0, 3),
            JsonSerializer.Serialize((context.CaseMemory?.Memories ?? []).Take(3).Select(x => new
            {
                id = x.Id,
                model = Sanitize(x.DeviceModel, 100, out _),
                category = Sanitize(x.ProblemCategory, 100, out _),
                confidence = Math.Round(x.Confidence, 3),
                relevance = Math.Round(x.Relevance, 3),
            }), JsonOptions),
            panelIdentityContextJson);
    }

    public static AiTrainingSourceDecision DeriveSourceDecision(
        bool ragUsed,
        bool visionUsed,
        bool diagnosticUsed,
        bool backendStateUsed)
    {
        if (visionUsed && ragUsed) return AiTrainingSourceDecision.VisionAndRag;
        if (diagnosticUsed && ragUsed) return AiTrainingSourceDecision.DiagnosticAndRag;
        if (backendStateUsed && ragUsed) return AiTrainingSourceDecision.BackendAndRag;
        if (ragUsed) return AiTrainingSourceDecision.Rag;
        if (visionUsed && (diagnosticUsed || backendStateUsed)) return AiTrainingSourceDecision.Mixed;
        if (visionUsed) return AiTrainingSourceDecision.Vision;
        if (diagnosticUsed) return AiTrainingSourceDecision.DiagnosticState;
        if (backendStateUsed) return AiTrainingSourceDecision.BackendState;
        return AiTrainingSourceDecision.GeneralKnowledge;
    }

    public static bool RequiresTechnicalVerification(
        string response,
        LedSupBot.Application.Abstractions.ConversationIntent intent,
        KnowledgeGroundingResult grounding)
    {
        if (grounding.ExactClaimNeedsModel || grounding.ConflictDetected)
        {
            return true;
        }

        if (ExactTechnicalValueRegex().IsMatch(response) || TechnicalCertaintyRegex().IsMatch(response))
        {
            return true;
        }

        return (intent is LedSupBot.Application.Abstractions.ConversationIntent.LedTechnicalQuestion
            or LedSupBot.Application.Abstractions.ConversationIntent.LedTroubleshooting)
            && grounding.Level == GroundingLevel.InsufficientEvidence;
    }

    /// <summary>
    /// Applies the same bounded privacy filtering to an operator-authored target as to the
    /// generation-time snapshot. Export-time anonymization is still required in T2.
    /// </summary>
    public static string SanitizePreferredResponse(string response, out bool containsRedactions) =>
        Sanitize(response, MaxPreferredResponseLength, out containsRedactions);

    public static bool ContainsPotentialTechnicalClaim(string response) =>
        ExactTechnicalValueRegex().IsMatch(response) || TechnicalCertaintyRegex().IsMatch(response);

    private static string BuildReplySource(bool ragUsed, bool visionUsed, bool ocrUsed, bool diagnosticUsed)
    {
        var parts = new List<string> { "Gemini" };
        if (diagnosticUsed) parts.Add("DiagnosticState");
        if (visionUsed) parts.Add("Vision");
        if (ocrUsed) parts.Add("OCR");
        if (ragUsed) parts.Add("RAG");
        return string.Join("+", parts);
    }

    private static string? BuildImageSummary(ValidatedImageAnalysis? image)
    {
        if (image is null) return null;
        var value = new
        {
            category = image.Analysis.Category.ToString(),
            status = image.Status.ToString(),
            source = image.Analysis.Source.ToString(),
            confidence = Math.Round(image.Analysis.Confidence, 4),
            validatedModels = image.ValidatedModels.Take(3).Select(x => Sanitize(x, 100, out _)),
            visibleConnections = image.Analysis.VisibleConnections.Take(6).Select(x => Sanitize(x, 160, out _)),
            visibleIndicators = image.Analysis.VisibleIndicators.Take(6).Select(x => Sanitize(x, 160, out _)),
            image.Analysis.OcrAttempted,
            image.Analysis.OcrSucceeded,
            image.Analysis.VisionAttempted,
            image.Analysis.VisionSucceeded,
        };
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static string SerializeStrings(IEnumerable<string> values) =>
        JsonSerializer.Serialize(
            values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Take(MaxFactCount)
                .Select(value => Sanitize(value, MaxFactLength, out _))
                .ToList(),
            JsonOptions);

    private static bool UsesBackendState(
        string customerMessage,
        string aiResponse,
        IReadOnlyList<string> knownFacts)
    {
        if (knownFacts.Count == 0)
        {
            return false;
        }

        var customerTokens = Tokenize(customerMessage);
        var responseTokens = Tokenize(aiResponse);
        var memoryQuestion = BackendMemoryQuestionRegex().IsMatch(customerMessage);

        foreach (var rawFact in knownFacts.Take(MaxFactCount))
        {
            var fact = Sanitize(rawFact, MaxFactLength, out _);
            if (fact.StartsWith("Sorun:", StringComparison.OrdinalIgnoreCase))
            {
                // Issue state is represented separately by DiagnosticState when it actually
                // participates in the generated response.
                continue;
            }

            var factTokens = Tokenize(fact);
            var unseenFactValueUsed = factTokens.Any(token =>
                !BackendMatchStopWords.Contains(token) &&
                (token.Any(char.IsDigit) || token.Length >= 4) &&
                responseTokens.Contains(token) &&
                !customerTokens.Contains(token));
            if (unseenFactValueUsed)
            {
                return true;
            }

            if (memoryQuestion)
            {
                var label = fact.Split(':', 2)[0];
                var labelTokens = Tokenize(label);
                if (labelTokens.Any(token => token.Length >= 4 && customerTokens.Contains(token)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool UsesDiagnosticState(
        string customerMessage,
        string aiResponse,
        string diagnosticContext)
    {
        if (string.IsNullOrWhiteSpace(diagnosticContext) ||
            string.Equals(diagnosticContext.Trim(), "none", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (DiagnosticContinuationRegex().IsMatch(customerMessage))
        {
            return true;
        }

        var customerTokens = Tokenize(customerMessage);
        var responseTokens = Tokenize(aiResponse);
        return Tokenize(diagnosticContext).Any(token =>
            !DiagnosticMatchStopWords.Contains(token) &&
            (token.Any(char.IsDigit) || token.Length >= 5) &&
            responseTokens.Contains(token) &&
            !customerTokens.Contains(token));
    }

    private static HashSet<string> Tokenize(string value) =>
        TokenRegex().Matches(value)
            .Select(match => match.Value.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string Sanitize(string? value, int maxLength, out bool redacted)
    {
        var result = value?.Trim() ?? string.Empty;
        redacted = false;
        if (result.Length > maxLength)
        {
            result = result[..maxLength];
        }

        result = Replace(result, PhoneOrAnyDeskRegex(), "[sensitive-number]", ref redacted);
        result = Replace(result, SecretLikeRegex(), "$1=[secret]", ref redacted);
        result = Replace(result, StandaloneCredentialRegex(), "[secret]", ref redacted);
        result = Replace(result, WindowsPathRegex(), "[local-path]", ref redacted);
        return result;
    }

    private static string Replace(string input, Regex regex, string replacement, ref bool redacted)
    {
        if (!regex.IsMatch(input)) return input;
        redacted = true;
        return regex.Replace(input, replacement);
    }

    private static string Bounded(string? value, int maxLength)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static string? BoundedOptional(string? value, int maxLength)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return null;
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static string SafeDocumentReference(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        var fileName = Path.GetFileName(text);
        return Bounded(string.IsNullOrWhiteSpace(fileName) ? text : fileName, 300);
    }

    private static string SafeWebReference(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return string.Empty;
        return Bounded(uri.GetLeftPart(UriPartial.Path), 500);
    }

    [GeneratedRegex(@"(?<!\d)(?:\+?\d[\s().-]?){7,15}(?!\d)")]
    private static partial Regex PhoneOrAnyDeskRegex();

    [GeneratedRegex(@"(?i)\b(api[_-]?key|token|secret|authorization|password|passwd|connector[_-]?secret)\s*[:=]\s*(?:bearer\s+)?[^\s;,]+")]
    private static partial Regex SecretLikeRegex();

    [GeneratedRegex(@"(?i)\b(?:sk-[a-z0-9_-]{12,}|AIza[a-z0-9_-]{20,}|eyJ[a-z0-9_-]{10,}\.[a-z0-9_-]{10,}\.[a-z0-9_-]{10,})\b")]
    private static partial Regex StandaloneCredentialRegex();

    [GeneratedRegex(@"(?i)\b[a-z]:\\(?:[^\s\\]+\\)*[^\s]+")]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex(@"(?i)(?:\b\d[\d.,]*\s*(?:(?:bin|milyon|million)\s+)?(?:piksel\w*|pixel\w*|hz|khz|mhz|fps|bit|gbps|mbps|watt|w|volt|v|amper|amp|a|port\w*|ohm|ms|mm|cm|metre|meter|nit|lumen|°c)\b|\b\d{2,5}\s*[x×]\s*\d{2,5}\b|\bip\s*\d{2,3}\b|\b(?:yüzde|percent)\s*\d[\d.,]*\b|\b\d[\d.,]*\s*%)")]
    private static partial Regex ExactTechnicalValueRegex();

    [GeneratedRegex(@"(?i)\b(kesin|kesinlikle|mutlaka|arızalıdır|arizalidir|bozuktur|destekler|maksimum)\b")]
    private static partial Regex TechnicalCertaintyRegex();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"(?i)\b(kaç|kac|hangi|neydi|kullanıyordum|kullaniyordum|kayıt(?:lı|ta)?|kayit(?:li|ta)?|daha\s+önce|daha\s+once|hatırla\w*|hatirla\w*|söylemiştim|soylemistim)\b")]
    private static partial Regex BackendMemoryQuestionRegex();

    private static readonly HashSet<string> DiagnosticMatchStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "activeissue", "customerresult", "hypotheses", "none", "outcome", "pendingcheck",
        "previouslytried", "status",
    };

    [GeneratedRegex(@"(?i)\b(yaptım|yaptim|denedim|uyguladım|uyguladim|aynı|ayni|değişmedi|degismedi|hâlâ|hala|olmadı|olmadi|düzelmedi|duzelmedi|sonuç|sonuc|işe\s+yaramadı|ise\s+yaramadi|yine)\b")]
    private static partial Regex DiagnosticContinuationRegex();
}
