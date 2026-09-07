using LedSupBot.Domain.Common;
using LedSupBot.Domain.PanelFiles;

namespace LedSupBot.Domain.Entities;

public sealed class PanelFilePackage : Entity
{
    public string PixelPitch { get; set; } = string.Empty;
    public string PanelCode { get; set; } = string.Empty;
    public string SecondaryCode { get; set; } = string.Empty;
    public string CanonicalKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? OriginalFolderName { get; set; }
    public DateTimeOffset? ImportedAt { get; set; }
    public string? Manufacturer { get; set; }
    public string? Description { get; set; }
    public PanelFilePackageStatus Status { get; set; } = PanelFilePackageStatus.Draft;
    public int Version { get; set; } = 1;
    public string? Notes { get; set; }
    public ICollection<PanelFilePackageAsset> Assets { get; set; } = new List<PanelFilePackageAsset>();
}

public sealed class PanelFilePackageAsset : Entity
{
    public Guid PanelFilePackageId { get; set; }
    public PanelFilePackage Package { get; set; } = null!;
    public PanelFileAssetType AssetType { get; set; }
    public PanelFileAssetStatus Status { get; set; } = PanelFileAssetStatus.Active;
    public int Version { get; set; } = 1;
    public string OriginalFileName { get; set; } = string.Empty;
    public string? RelativePath { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string MimeType { get; set; } = "application/octet-stream";
    public long FileSize { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}
