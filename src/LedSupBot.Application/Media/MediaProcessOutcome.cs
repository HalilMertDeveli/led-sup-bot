namespace LedSupBot.Application.Media;

public enum MediaProcessOutcome
{
    Stored = 0,
    AlreadyProcessed = 1,
    UnsupportedMimeType = 2,
    FileTooLarge = 3,
    InvalidImage = 4,
    DownloadFailed = 5,
    NotDownloadable = 6,
    StorageFailed = 7,
    SkippedNonCustomer = 8,
}

public sealed record InboundMediaProcessResult(
    MediaProcessOutcome Outcome,
    string? StorageKey = null,
    string? ContentType = null,
    long? SizeBytes = null,
    Guid? PanelLabelPhotoId = null);
