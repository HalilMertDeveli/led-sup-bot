using LedSupBot.Domain.Common;
using LedSupBot.Domain.Enums;

namespace LedSupBot.Domain.Entities;

public sealed class KnowledgeDocument : Entity
{
    public string Manufacturer { get; set; } = "Colorlight";
    public string ProductType { get; set; } = string.Empty;
    public string ProductFamily { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string DocumentType { get; set; } = "Specification";
    public string DocumentVersion { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public string OriginalFileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public string ExtractionMethod { get; set; } = string.Empty;
    public string ExtractionVersion { get; set; } = string.Empty;
    public ExtractionQuality ExtractionQuality { get; set; }
    public KnowledgeDocumentStatus Status { get; set; } = KnowledgeDocumentStatus.Pending;
    public DateTimeOffset? ImportedAtUtc { get; set; }
    public ICollection<KnowledgeChunk> Chunks { get; set; } = new List<KnowledgeChunk>();
}
