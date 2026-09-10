namespace LedSupBot.Infrastructure.Knowledge;

public sealed class KnowledgeOptions
{
    public const string SectionName = "Knowledge";
    public string RootPath { get; set; } = @"E:\led-sup-bot\knowledge\raw";
    public int ChunkTargetCharacters { get; set; } = 1800;
    public int ChunkOverlapCharacters { get; set; } = 180;
    public FallbackExtractionOptions FallbackExtraction { get; set; } = new();
}
public sealed class FallbackExtractionOptions
{
    public string Provider { get; set; } = "Gemini";
    public string Model { get; set; } = string.Empty;
    public string PipelineVersion { get; set; } = "vision-pdf-v1";
    public string CachePath { get; set; } = @"E:\led-sup-bot\knowledge\cache\fallback";
    public int PagesPerRequest { get; set; } = 4;
    public int MaxConcurrentPages { get; set; } = 2;
    public int RetryCount { get; set; } = 3;
}
public sealed class EmbeddingOptions
{
    public const string SectionName = "Embedding";
    public string Provider { get; set; } = "Gemini";
    public string Model { get; set; } = "gemini-embedding-001";
    public int Dimensions { get; set; } = 768;
    public int BatchSize { get; set; } = 20;
}
