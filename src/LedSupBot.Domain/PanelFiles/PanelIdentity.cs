namespace LedSupBot.Domain.PanelFiles;

public sealed record PanelIdentity(string? PixelPitch, string? PanelCode, string? SecondaryCode)
{
    public bool IsComplete => !string.IsNullOrWhiteSpace(PixelPitch) &&
                              !string.IsNullOrWhiteSpace(PanelCode) &&
                              !string.IsNullOrWhiteSpace(SecondaryCode);

    public string? CanonicalKey => IsComplete
        ? $"{PixelPitch}-{PanelCode}-{SecondaryCode}"
        : null;
}

public enum PanelFilePackageStatus { Draft = 0, Active = 1, Archived = 2 }
public enum PanelFileAssetType { FirmwareHex = 0, ReceiverConfigRcvbp = 1, PackageFile = 2 }
public enum PanelFileAssetStatus { Active = 0, Archived = 1 }
