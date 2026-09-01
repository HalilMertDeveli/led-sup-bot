namespace LedSupBot.Infrastructure.Configuration;

/// <summary>Local private media storage (Phase 7). Not publicly served.</summary>
public sealed class MediaStorageOptions
{
    public const string SectionName = "MediaStorage";

    /// <summary>Root directory for stored media. Relative paths resolve from content root. Env: MEDIA_STORAGE_ROOT.</summary>
    public string RootPath { get; set; } = "./data/media";

    /// <summary>Maximum inbound image size in bytes. Env: MEDIA_MAX_FILE_SIZE_BYTES.</summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Retention days (0 = no automatic deletion). Env: MEDIA_RETENTION_DAYS.</summary>
    public int RetentionDays { get; set; }
}
