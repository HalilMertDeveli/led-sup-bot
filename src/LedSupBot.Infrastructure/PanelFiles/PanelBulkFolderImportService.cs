using LedSupBot.Application.PanelFiles;
using Microsoft.Extensions.Logging;

namespace LedSupBot.Infrastructure.PanelFiles;

/// <summary>
/// Server-side bulk folder import. Discovery is first-level only; every valid
/// child folder is imported through the SAME authoritative single-folder
/// <see cref="IPanelFolderImportService"/>, so no business rule is duplicated
/// and each package import stays atomic.
/// </summary>
public sealed class PanelBulkFolderImportService(
    IPanelFolderImportService importService,
    ILogger<PanelBulkFolderImportService> logger) : IPanelBulkFolderImportService
{
    public async Task<PanelBulkImportResult> ImportAllAsync(string parentFolder,
        CancellationToken cancellationToken = default)
    {
        var candidates = PanelBulkFolderDiscovery.Discover(parentFolder);
        var results = new List<PanelBulkImportItemResult>();
        var imported = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var candidate in candidates)
        {
            if (candidate.Status is not ("READY" or "REIMPORT"))
            {
                skipped++;
                results.Add(new PanelBulkImportItemResult(
                    candidate.FolderName, candidate.CanonicalKey, "SkippedInvalid", candidate.Error));
                continue;
            }

            try
            {
                var files = ReadFiles(candidate.FolderPath);
                try
                {
                    await importService.ImportAsync(candidate.FolderName, files, cancellationToken);
                }
                finally
                {
                    foreach (var file in files)
                    {
                        await file.Content.DisposeAsync();
                    }
                }

                imported++;
                results.Add(new PanelBulkImportItemResult(candidate.FolderName, candidate.CanonicalKey, "Imported"));
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogWarning(
                    ex,
                    "Bulk panel folder import failed for folder {FolderName} canonicalKey={CanonicalKey}",
                    candidate.FolderName,
                    candidate.CanonicalKey ?? "unparsed");
                results.Add(new PanelBulkImportItemResult(candidate.FolderName, candidate.CanonicalKey, "Failed", ex.Message));
            }
        }

        logger.LogInformation(
            "Bulk panel folder import completed found={Found} imported={Imported} skipped={Skipped} failed={Failed}",
            candidates.Count, imported, skipped, failed);
        return new PanelBulkImportResult(candidates.Count, imported, skipped, failed, results);
    }

    private static List<PanelFolderImportFile> ReadFiles(string folderPath)
    {
        var result = new List<PanelFolderImportFile>();
        foreach (var path in Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            var fileName = Path.GetFileName(path);
            result.Add(new PanelFolderImportFile(fileName, fileName, GuessContentType(fileName), File.OpenRead(path)));
        }

        return result;
    }

    private static string GuessContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".hex" => "application/octet-stream",
            ".rcvbp" => "application/octet-stream",
            _ => "application/octet-stream",
        };
    }
}