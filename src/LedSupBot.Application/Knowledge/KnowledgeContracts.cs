namespace LedSupBot.Application.Knowledge;

public sealed record PdfPageText(int PageNumber, string Text, bool VisuallyNonEmpty = true);
public sealed record PdfExtractionResult(IReadOnlyList<PdfPageText> Pages, string Method);
public interface IPdfKnowledgeExtractor { Task<PdfExtractionResult> ExtractAsync(string path, CancellationToken cancellationToken); }
public interface IDocumentFallbackExtractor { Task<PdfExtractionResult?> ExtractAsync(string path, CancellationToken cancellationToken); }
public interface IEmbeddingService
{
    string Provider { get; }
    string Model { get; }
    int Dimensions { get; }
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}
public sealed record TechnicalKnowledgeQuery(string Text, string? Manufacturer = null, string? ProductType = null, string? ProductFamily = null, string? Model = null, int TopK = 5);
public sealed record TechnicalKnowledgeResult(string Content, double Score, string Document, string Model, int PageStart, int PageEnd, string SectionTitle);
public interface ITechnicalKnowledgeRetriever { Task<IReadOnlyList<TechnicalKnowledgeResult>> SearchAsync(TechnicalKnowledgeQuery query, CancellationToken cancellationToken); }
