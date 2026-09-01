using LedSupBot.Domain.Common;
using LedSupBot.Domain.Enums;

namespace LedSupBot.Domain.Entities;

public class PanelLabelPhoto : Entity
{
    public Guid TechnicalSupportCaseId { get; set; }

    public TechnicalSupportCase TechnicalSupportCase { get; set; } = null!;

    /// <summary>Private object-storage key — not a public URL.</summary>
    public string StorageKey { get; set; } = string.Empty;

    public string? OriginalFileName { get; set; }

    public string ContentType { get; set; } = "application/octet-stream";

    public long? SizeBytes { get; set; }

    public int SortOrder { get; set; }

    public MediaAnalysisStatus AnalysisStatus { get; set; } = MediaAnalysisStatus.NotStarted;

    /// <summary>Optional JSON blob or storage pointer for vision output (Phase 7).</summary>
    public string? AnalysisResultReference { get; set; }
}
