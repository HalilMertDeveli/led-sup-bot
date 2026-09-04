using System.Text.Json;
using System.Text.RegularExpressions;
using LedSupBot.Application.Abstractions;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LedSupBot.Application.Media;

public enum TechnicalImageCategory { PanelLabel, ReceiverLabel, ControllerLabel, ProcessorLabel, DeviceLabel, ConnectionPhoto, SoftwareScreenshot, UnknownTechnicalImage }
public enum ImageAnalysisSource { Ocr, Vision, OcrAndVision }
public enum ImageCandidateStatus { Verified, Probable, Ambiguous, Rejected }

public sealed record TechnicalImageAnalysisResult(
    TechnicalImageCategory Category,
    string DetectedText,
    string? CandidateManufacturer,
    IReadOnlyList<string> CandidateModels,
    string? CandidateSerialNumber,
    string? CandidateFirmwareVersion,
    string? CandidateHardwareVersion,
    IReadOnlyList<string> CandidatePorts,
    IReadOnlyList<string> VisibleConnections,
    IReadOnlyList<string> VisibleIndicators,
    double Confidence,
    IReadOnlyList<string> Warnings,
    ImageAnalysisSource Source,
    bool OcrAttempted = false,
    bool OcrSucceeded = false,
    bool VisionAttempted = false,
    bool VisionSucceeded = false);

public sealed record ValidatedImageAnalysis(
    TechnicalImageAnalysisResult Analysis,
    ImageCandidateStatus Status,
    IReadOnlyList<string> ValidatedModels,
    string? CustomerClarification)
{
    public string ToOperatorTrace() =>
        $"Image analysis attempted=True; OCR attempted={Analysis.OcrAttempted}; OCR success={Analysis.OcrSucceeded}; Vision attempted={Analysis.VisionAttempted}; Vision success={Analysis.VisionSucceeded}; category={Analysis.Category}; candidates={string.Join(',', Analysis.CandidateModels)}; validatedModel={string.Join(',', ValidatedModels)}; confidence={Analysis.Confidence:0.000}; warnings={string.Join(',', Analysis.Warnings)}";
}

public interface ITechnicalImageAnalyzer
{
    Task<TechnicalImageAnalysisResult?> AnalyzeAsync(string storageKey, string contentType, string? caption, CancellationToken cancellationToken);
}

public sealed class TechnicalImageAnalysisService
{
    private readonly IApplicationDbContext _db;
    private readonly ITechnicalImageAnalyzer? _analyzer;
    private readonly ILogger<TechnicalImageAnalysisService> _logger;

    public TechnicalImageAnalysisService(IApplicationDbContext db, IEnumerable<ITechnicalImageAnalyzer> analyzers,
        ILogger<TechnicalImageAnalysisService> logger)
    { _db = db; _analyzer = analyzers.LastOrDefault(); _logger = logger; }

    public async Task<ValidatedImageAnalysis?> AnalyzeAsync(IReadOnlyList<Guid> messageIds, string? caption, CancellationToken cancellationToken)
    {
        var message = await _db.Messages.AsNoTracking().Where(x => messageIds.Contains(x.Id) && x.MessageType == MessageType.Image && x.StorageKey != null)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (message?.MessageType != MessageType.Image || string.IsNullOrWhiteSpace(message.StorageKey)) return null;
        var photo = await _db.PanelLabelPhotos.SingleOrDefaultAsync(x => x.StorageKey == message.StorageKey, cancellationToken);
        if (photo is null) return null;
        if (photo.AnalysisStatus == MediaAnalysisStatus.Completed && !string.IsNullOrWhiteSpace(photo.AnalysisResultReference))
        {
            try { return JsonSerializer.Deserialize<ValidatedImageAnalysis>(photo.AnalysisResultReference); }
            catch (JsonException) { /* Retry one invalid legacy result. */ }
        }
        if (_analyzer is null) return await CompleteAsync(photo, Unavailable("vision_provider_unavailable"), cancellationToken);
        TechnicalImageAnalysisResult? raw;
        try { raw = await _analyzer.AnalyzeAsync(photo.StorageKey, photo.ContentType, caption, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _logger.LogWarning(ex, "Technical image analysis failed for photo {PhotoId}", photo.Id); raw = null; }
        var documentedModels = await _db.KnowledgeDocuments.AsNoTracking().Where(x => x.Status == KnowledgeDocumentStatus.Ready)
            .Select(x => x.Model).Distinct().ToListAsync(cancellationToken);
        return await CompleteAsync(photo, Validate(raw, documentedModels), cancellationToken);
    }

    private async Task<ValidatedImageAnalysis> CompleteAsync(PanelLabelPhoto photo, ValidatedImageAnalysis result, CancellationToken ct)
    {
        photo.AnalysisStatus = MediaAnalysisStatus.Completed;
        photo.AnalysisResultReference = JsonSerializer.Serialize(result);
        await _db.SaveChangesAsync(ct);
        return result;
    }

    private static ValidatedImageAnalysis Validate(TechnicalImageAnalysisResult? analysis, IReadOnlyCollection<string> documentedModels)
    {
        if (analysis is null) return Unavailable("analysis_failed");
        var normalized = analysis.CandidateModels.Select(NormalizeCandidate).Where(x => x is not null).Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
        var verified = normalized.Where(model => IsStrictSupportedModel(model) || documentedModels.Contains(model, StringComparer.OrdinalIgnoreCase)).ToList();
        var status = verified.Count == 1 && analysis.Confidence >= .78 ? ImageCandidateStatus.Verified
            : verified.Count > 0 ? ImageCandidateStatus.Probable
            : analysis.CandidateModels.Count > 0 ? ImageCandidateStatus.Ambiguous : ImageCandidateStatus.Rejected;
        var accepted = status == ImageCandidateStatus.Verified ? verified : [];
        var clarification = accepted.Count == 0
            ? "Model yazısı net görünmüyor. Etiketi biraz daha yakından çekebilir veya modeli yazabilir misiniz?" : null;
        return new(analysis, status, accepted, clarification);
    }

    private static ValidatedImageAnalysis Unavailable(string warning)
    {
        var analysis = new TechnicalImageAnalysisResult(TechnicalImageCategory.UnknownTechnicalImage, "", null, [], null, null, null,
            [], [], [], 0, [warning], ImageAnalysisSource.Vision, VisionAttempted: true);
        return new(analysis, ImageCandidateStatus.Rejected, [], "Fotoğrafı okuyamadım. Etiketi daha yakından çekebilir veya modeli yazabilir misiniz?");
    }

    private static string? NormalizeCandidate(string value)
    {
        var token = Regex.Replace(value.Trim(), @"\s+", "");
        if (Regex.IsMatch(token, @"(?i)^E12O$")) return "E120";
        if (Regex.IsMatch(token, @"(?i)^5A75[£€]$")) return "5A75E";
        if (!Regex.IsMatch(token, @"(?i)^(?:X\d+[a-z]?|E\d+|5A-?75E|Z\d+(?:ProG\d+)?|S\d+[a-z]?)$")) return null;
        if (Regex.IsMatch(token, @"(?i)^X\d+[ems]$")) return char.ToUpperInvariant(token[0]) + token[1..^1] + char.ToLowerInvariant(token[^1]);
        return token.ToUpperInvariant();
    }

    private static bool IsStrictSupportedModel(string model) =>
        Regex.IsMatch(model, @"(?i)^(?:X4e|X20m?|E(?:80|120)|5A-?75E)$");
}
