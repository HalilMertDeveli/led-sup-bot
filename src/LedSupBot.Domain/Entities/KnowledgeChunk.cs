using LedSupBot.Domain.Common;
using Pgvector;

namespace LedSupBot.Domain.Entities;

public sealed class KnowledgeChunk : Entity
{
    public Guid DocumentId { get; set; }
    public KnowledgeDocument Document { get; set; } = null!;
    public int ChunkIndex { get; set; }
    public int PageStart { get; set; }
    public int PageEnd { get; set; }
    public string SectionTitle { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public Vector Embedding { get; set; } = new(Array.Empty<float>());
}
