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
    Task<PanelFilePackage?> RollbackAsync(Guid packageId, CancellationToken cancellationToken = default);
}

public sealed class PanelFilePackageOptions
{
    public const string SectionName = "PanelFilePackages";
    public long MaxFirmwareFileSizeBytes { get; set; } = 16 * 1024 * 1024;
    public long MaxReceiverConfigFileSizeBytes { get; set; } = 8 * 1024 * 1024;
    public long MaxImportedFileSizeBytes { get; set; } = 25 * 1024 * 1024;
    public int MinImportedFileCount { get; set; } = 2;
}

public sealed record PanelFolderPackageName(string OriginalName, string PixelPitch, string PanelCode, string SecondaryCode);

public static class PanelFolderPackageNameParser
{
    public static PanelFolderPackageName Parse(string? folderName)
    {
        var value = (folderName ?? string.Empty).Trim();
        if (value.Length is 0 or > 255 || value.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new InvalidDataException("Bu klasör adı beklenen P+Chipset+Decoder formatında değil.");

        var parts = value.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace) ||
            !System.Text.RegularExpressions.Regex.IsMatch(parts[0], @"(?i)^P\d+(?:[.,]\d{1,3})?$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(parts[1], @"(?i)^[A-Z0-9]{2,32}$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(parts[2], @"(?i)^[A-Z0-9]{2,32}$"))
            throw new InvalidDataException("Bu klasör adı beklenen P+Chipset+Decoder formatında değil.");

        return new(value, parts[0], parts[1], parts[2]);
    }
}

public static class PanelFolderImportMultipartFactory
{
    public static MultipartFormDataContent Create(string folderPath)
    {
        var root = Path.GetFullPath(folderPath);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Create(Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)), files);
    }

    public static MultipartFormDataContent Create(string folderName, IReadOnlyList<string> selectedFilePaths)
    {
        var packageName = PanelFolderPackageNameParser.Parse(folderName);
        var files = selectedFilePaths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
        ValidateMinimumAssets(files.Select(path => Path.GetFileName(path)!).ToList());

        var content = new MultipartFormDataContent();
        try
        {
            content.Add(new StringContent(packageName.OriginalName), "folderName");
            foreach (var path in files)
            {
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Bağlantı/reparse dosyaları içe aktarılamaz.");
                var part = new StreamContent(File.OpenRead(path));
                part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                content.Add(part, "files", Path.GetFileName(path));
            }
            return content;
        }
        catch
        {
            content.Dispose();
            throw;
        }
    }

    public static void ValidateMinimumAssets(IReadOnlyList<string> fileNames)
    {
        if (fileNames.Count < 2)
            throw new InvalidDataException("Pakette en az 2 dosya olmalı.");
    }
}

public sealed record PanelFolderImportFile(string RelativePath, string FileName, string ContentType, Stream Content);

public sealed record PanelBulkFolderCandidate(string FolderPath, string FolderName, string? CanonicalKey,
    int FileCount, string Status, string? Error = null);

public sealed record PanelImportSelectionPackage(string FolderPath, string FolderName, string? CanonicalKey,
    IReadOnlyList<string> FilePaths, IReadOnlyList<string> SelectedPaths, string Status, string? Error = null)
{
    public bool CanImport => Status is "READY" or "REIMPORT";
}

public sealed record PanelImportSelectionPlan(IReadOnlyList<PanelImportSelectionPackage> Packages)
{
    public IReadOnlyList<PanelImportSelectionPackage> ImportablePackages => Packages.Where(x => x.CanImport).ToList();
}

/// <summary>
/// Converts an Explorer-style mixture of selected folders and files into package imports.
/// Package identity always comes from the selected folder or a selected file's immediate
/// parent. Paths are normalized and de-duplicated before a request is built.
/// </summary>
public static class PanelImportSelectionNormalizer
{
    public static PanelImportSelectionPlan Normalize(IEnumerable<string> selectedPaths,
        IReadOnlySet<string>? existingCanonicalKeys = null)
    {
        ArgumentNullException.ThrowIfNull(selectedPaths);
        var groups = new Dictionary<string, SelectionGroup>(StringComparer.OrdinalIgnoreCase);
        var invalid = new List<PanelImportSelectionPackage>();

        foreach (var rawPath in selectedPaths.Where(x => !string.IsNullOrWhiteSpace(x))
                     .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(rawPath))
            {
                AddDirectory(rawPath, groups, invalid);
                continue;
            }

            if (File.Exists(rawPath))
            {
                AddFile(rawPath, rawPath, groups, invalid);
                continue;
            }

            invalid.Add(new(rawPath, Path.GetFileName(rawPath), null, [], [rawPath],
                "INVALID", "Seçilen öğe artık mevcut değil."));
        }

        var packages = groups.Values
            .Select(group => Build(group, existingCanonicalKeys))
            .Concat(invalid)
            .OrderBy(x => x.FolderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Status, StringComparer.Ordinal)
            .ToList();
        return new PanelImportSelectionPlan(packages);
    }

    public static bool TryGetCanonicalKey(string directoryPath, out string canonicalKey)
    {
        canonicalKey = string.Empty;
        try
        {
            var root = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parsed = PanelFolderPackageNameParser.Parse(Path.GetFileName(root));
            canonicalKey = $"{parsed.PixelPitch.ToUpperInvariant().Replace(',', '.')}" +
                           $"-{parsed.PanelCode.ToUpperInvariant()}-{parsed.SecondaryCode.ToUpperInvariant()}";
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static void AddDirectory(string directoryPath, IDictionary<string, SelectionGroup> groups,
        ICollection<PanelImportSelectionPackage> invalid)
    {
        try
        {
            var info = new DirectoryInfo(directoryPath);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Bağlantı/reparse klasörleri içe aktarılamaz.");
            if (!TryGetCanonicalKey(directoryPath, out _))
                throw new InvalidDataException("PACKAGE IDENTITY REQUIRED");

            var files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFullPath).ToList();
            foreach (var filePath in files)
            {
                var file = new FileInfo(filePath);
                if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Bağlantı/reparse dosyaları içe aktarılamaz.");
            }
            var group = GetGroup(directoryPath, groups);
            group.SelectedPaths.Add(directoryPath);
            foreach (var filePath in files) group.FilePaths.Add(filePath);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            invalid.Add(new(directoryPath, Path.GetFileName(directoryPath), null, [], [directoryPath],
                "NO PACKAGE", ex.Message));
        }
    }

    private static void AddFile(string filePath, string selectedPath, IDictionary<string, SelectionGroup> groups,
        ICollection<PanelImportSelectionPackage> invalid)
    {
        var parent = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(parent) || !TryGetCanonicalKey(parent, out _))
        {
            invalid.Add(new(parent ?? filePath, Path.GetFileName(filePath), null, [filePath], [selectedPath],
                "NO PACKAGE", "PACKAGE IDENTITY REQUIRED"));
            return;
        }

        try
        {
            var group = GetGroup(parent, groups);
            group.SelectedPaths.Add(selectedPath);
            AddEligibleFile(filePath, group);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            invalid.Add(new(parent, Path.GetFileName(filePath), null, [filePath], [selectedPath],
                "INVALID", ex.Message));
        }
    }

    private static void AddEligibleFile(string filePath, SelectionGroup group)
    {
        var fullPath = Path.GetFullPath(filePath);
        var info = new FileInfo(fullPath);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Bağlantı/reparse dosyaları içe aktarılamaz.");
        group.FilePaths.Add(fullPath);
    }

    private static SelectionGroup GetGroup(string folderPath, IDictionary<string, SelectionGroup> groups)
    {
        var normalized = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!groups.TryGetValue(normalized, out var group))
        {
            group = new SelectionGroup(normalized);
            groups.Add(normalized, group);
        }
        return group;
    }

    private static PanelImportSelectionPackage Build(SelectionGroup group, IReadOnlySet<string>? existing)
    {
        var folderName = Path.GetFileName(group.FolderPath);
        _ = TryGetCanonicalKey(group.FolderPath, out var canonicalKey);
        var files = group.FilePaths.OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase).ToList();
        var selected = group.SelectedPaths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count < 2)
            return new(group.FolderPath, folderName, canonicalKey, files, selected, "INVALID", "En az 2 dosya gerekli.");
        return new(group.FolderPath, folderName, canonicalKey, files, selected,
            existing?.Contains(canonicalKey) == true ? "REIMPORT" : "READY");
    }

    private sealed class SelectionGroup(string folderPath)
    {
        public string FolderPath { get; } = folderPath;
        public HashSet<string> FilePaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SelectedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

public static class PanelBulkFolderDiscovery
{
    public static IReadOnlyList<PanelBulkFolderCandidate> Discover(string parentFolder,
        IReadOnlySet<string>? existingCanonicalKeys = null)
    {
        var root = Path.GetFullPath(parentFolder);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Üst klasör bulunamadı.");
        return Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase)
            .Select(path => Build(path, existingCanonicalKeys)).ToList();
    }

    private static PanelBulkFolderCandidate Build(string path, IReadOnlySet<string>? existing)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        try
        {
            var parsed = PanelFolderPackageNameParser.Parse(name);
            var files = Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)
                .Count(file => (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0);
            if (files < 2) return new(path, name, null, files, "INVALID", "En az 2 dosya gerekli.");
            var canonical = $"{parsed.PixelPitch.ToUpperInvariant().Replace(',', '.')}" +
                            $"-{parsed.PanelCode.ToUpperInvariant()}-{parsed.SecondaryCode.ToUpperInvariant()}";
            return new(path, name, canonical, files,
                existing?.Contains(canonical) == true ? "REIMPORT" : "READY");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new(path, name, null, 0, "INVALID", ex.Message);
        }
    }
}
public interface IPanelFolderImportService
{
    Task<PanelFilePackage> ImportAsync(string folderName, IReadOnlyList<PanelFolderImportFile> files,
        CancellationToken cancellationToken = default);
}

public sealed record PanelBulkImportItemResult(string FolderName, string? CanonicalKey, string Status, string? Error = null);

public sealed record PanelBulkImportResult(int Found, int Imported, int Skipped, int Failed,
    IReadOnlyList<PanelBulkImportItemResult> Results);

/// <summary>
/// Bulk folder import. Discovers first-level child folders and reuses the
/// SAME authoritative single-folder import service (<see cref="IPanelFolderImportService"/>)
/// for every candidate. Each package import remains atomic; invalid folders are
/// skipped without blocking valid ones.
/// </summary>
public interface IPanelBulkFolderImportService
{
    Task<PanelBulkImportResult> ImportAllAsync(string parentFolder, CancellationToken cancellationToken = default);
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
