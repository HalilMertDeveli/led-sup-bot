using LedSupBot.Domain.Entities;
using LedSupBot.Domain.PanelFiles;

namespace LedSupBot.Application.PanelFiles;

public enum PanelFilePackageResolutionStatus
{
    ExactMatch = 0, NoMatch = 1, MissingInformation = 2,
    IncompletePackage = 3, InactivePackage = 4, Ambiguous = 5
}

public sealed record PanelFilePackageResolution(
    PanelFilePackageResolutionStatus Status,
    Guid? PackageId = null,
    string? CanonicalKey = null,
    IReadOnlyList<PanelFilePackageAsset>? Assets = null,
    bool IsFolderPackage = false);

public interface IPanelFilePackageResolver
{
    Task<PanelFilePackageResolution> ResolveAsync(PanelIdentity identity, CancellationToken cancellationToken = default);
}

public static class PanelFileAssetPolicy
{
    public static bool TryClassify(string? fileName, out PanelFileAssetType type)
    {
        type = default;
        var candidate = fileName ?? string.Empty;
        if (candidate.Contains('/') || candidate.Contains('\\')) return false;
        var safeName = Path.GetFileName(candidate);
        var stem = Path.GetFileNameWithoutExtension(safeName);
        if (string.IsNullOrWhiteSpace(stem) || stem.Contains('.', StringComparison.Ordinal)) return false;
        var extension = Path.GetExtension(safeName);
        if (extension.Equals(".hex", StringComparison.OrdinalIgnoreCase)) { type = PanelFileAssetType.FirmwareHex; return true; }
        if (extension.Equals(".rcvbp", StringComparison.OrdinalIgnoreCase)) { type = PanelFileAssetType.ReceiverConfigRcvbp; return true; }
        return false;
    }
}

public interface IPanelFileAssetRegistry
{
    Task<PanelFilePackageAsset> StoreAsync(Guid packageId, PanelFileAssetType expectedType, string originalFileName, Stream content,
        long maxSizeBytes = 25 * 1024 * 1024, CancellationToken cancellationToken = default);
}

public interface IPanelFilePackageRegistry
{
    Task<PanelFilePackage> CreateDraftAsync(string pixelPitch, string panelCode, string secondaryCode,
        string? displayName = null, CancellationToken cancellationToken = default);
    Task<bool> ActivateAsync(Guid packageId, CancellationToken cancellationToken = default);
    Task<bool> ArchiveAsync(Guid packageId, CancellationToken cancellationToken = default);
    Task<PanelFilePackage?> UpdateDraftAsync(Guid packageId, string pixelPitch, string panelCode, string secondaryCode,
        string? displayName, string? manufacturer, string? description, string? notes, int version,
        CancellationToken cancellationToken = default);
}

public sealed class PanelFilePackageOptions
{
    public const string SectionName = "PanelFilePackages";
    public long MaxFirmwareFileSizeBytes { get; set; } = 16 * 1024 * 1024;
    public long MaxReceiverConfigFileSizeBytes { get; set; } = 8 * 1024 * 1024;
    public long MaxImportedFileSizeBytes { get; set; } = 25 * 1024 * 1024;
    public int MinImportedFileCount { get; set; } = 2;
    public int MaxImportedFileCount { get; set; } = 3;
}

public sealed record PanelFolderImportFile(string RelativePath, string FileName, string ContentType, Stream Content);
public interface IPanelFolderImportService
{
    Task<PanelFilePackage> ImportAsync(string folderName, IReadOnlyList<PanelFolderImportFile> files,
        CancellationToken cancellationToken = default);
}

public sealed record PanelFileAssetAdminDto(Guid Id, PanelFileAssetType AssetType, string OriginalFileName,
    string MimeType, long FileSize, string Sha256, int Version, PanelFileAssetStatus Status, DateTimeOffset CreatedAt);
public sealed record PanelFilePackageAdminDto(Guid Id, string PixelPitch, string PanelCode, string SecondaryCode,
    string CanonicalKey, string DisplayName, string? Manufacturer, string? Description, string? Notes,
    PanelFilePackageStatus Status, int Version, string Readiness, IReadOnlyList<PanelFileAssetAdminDto> Assets, DateTimeOffset UpdatedAt,
    string? OriginalFolderName = null, DateTimeOffset? ImportedAt = null);
public sealed record PanelFilePackageListResult(IReadOnlyList<PanelFilePackageAdminDto> Items, int Total);

public interface IPanelFilePackageAdminQuery
{
    Task<PanelFilePackageListResult> ListAsync(string? search, string? pitch, string? code,
        PanelFilePackageStatus? status, string? readiness, int limit, CancellationToken cancellationToken = default);
    Task<PanelFilePackageAdminDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);
}
