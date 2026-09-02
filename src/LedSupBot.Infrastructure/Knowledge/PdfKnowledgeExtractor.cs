using LedSupBot.Application.Knowledge;
using UglyToad.PdfPig;

namespace LedSupBot.Infrastructure.Knowledge;

public sealed class PdfKnowledgeExtractor : IPdfKnowledgeExtractor
{
    public Task<PdfExtractionResult> ExtractAsync(string path, CancellationToken cancellationToken)
    {
        var pages = new List<PdfPageText>();
        using var pdf = PdfDocument.Open(path);
        foreach (var page in pdf.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            pages.Add(new(page.Number, page.Text));
        }
        return Task.FromResult(new PdfExtractionResult(pages, "PdfPig-native"));
    }
}

public sealed class UnavailableDocumentFallbackExtractor : IDocumentFallbackExtractor
{
    public Task<PdfExtractionResult?> ExtractAsync(string path, CancellationToken cancellationToken) => Task.FromResult<PdfExtractionResult?>(null);
}
