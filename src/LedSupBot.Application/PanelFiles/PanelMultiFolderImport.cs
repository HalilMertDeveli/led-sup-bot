using System.IO;
using System.Text;

namespace LedSupBot.Application.PanelFiles;

public enum PanelMultiFolderItemStatus
{
    Added = 0,
    SkippedExisting = 1,
    Failed = 2
}

public sealed record PanelMultiFolderItemResult(string FolderPath, string FolderName,
    PanelMultiFolderItemStatus Status, string? Error = null);

public sealed record PanelMultiFolderBatchResult(int SelectedCount, int AddedCount, int SkippedExistingCount,
    int FailedCount, IReadOnlyList<PanelMultiFolderItemResult> Items);

/// <summary>
/// Folder-centric bulk add coordinator. Every unique selected folder is processed through the
/// SAME authoritative single-folder import pipeline (supplied as a delegate, mirroring
/// <see cref="IPanelFolderImportService"/> usage on the client). Windows paths are normalized
/// (trailing separators and casing) before de-duplication; folders whose canonical package key
/// is already configured are skipped; each remaining folder is imported sequentially so one
/// invalid or inaccessible folder never aborts the rest of the batch.
/// </summary>
public static class PanelMultiFolderImport
{
    /// <summary>Windows-safe canonical form: full path with trailing separators trimmed (drive root kept).</summary>
    public static string NormalizeFolderPath(string folderPath)
    {
        var full = Path.GetFullPath(folderPath);
        var trimmed = Path.TrimEndingDirectorySeparator(full);
        return string.IsNullOrEmpty(trimmed) ? full : trimmed;
    }

    /// <summary>Normalizes every path and removes duplicates inside the batch (case-insensitive on Windows).</summary>
    public static IReadOnlyList<string> NormalizeDistinct(IEnumerable<string> folderPaths)
    {
        ArgumentNullException.ThrowIfNull(folderPaths);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        foreach (var raw in folderPaths)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var normalized = NormalizeFolderPath(raw);
            if (seen.Add(normalized)) ordered.Add(normalized);
        }
        return ordered;
    }

    /// <summary>
    /// Runs the batch: normalize → distinct → skip already-configured (canonical key) →
    /// import each remaining folder through the single-folder pipeline → per-folder failure isolation.
    /// </summary>
    public static async Task<PanelMultiFolderBatchResult> RunAsync(
        IEnumerable<string> selectedFolderPaths,
        IEnumerable<string?>? existingCanonicalKeys,
        Func<string, CancellationToken, Task> importFolderAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(importFolderAsync);
        var selected = NormalizeDistinct(selectedFolderPaths);
        var existing = new HashSet<string>(
            (existingCanonicalKeys ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!),
            StringComparer.OrdinalIgnoreCase);

        var items = new List<PanelMultiFolderItemResult>(selected.Count);
        foreach (var folder in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folderName = Path.GetFileName(folder);
            PanelMultiFolderItemResult outcome;
            try
            {
                if (PanelImportSelectionNormalizer.TryGetCanonicalKey(folder, out var canonicalKey)
                    && existing.Contains(canonicalKey))
                {
                    outcome = new(folder, folderName, PanelMultiFolderItemStatus.SkippedExisting);
                }
                else
                {
                    await importFolderAsync(folder, cancellationToken);
                    outcome = new(folder, folderName, PanelMultiFolderItemStatus.Added);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                outcome = new(folder, folderName, PanelMultiFolderItemStatus.Failed, ex.Message);
            }
            items.Add(outcome);
        }

        return new(
            selected.Count,
            items.Count(x => x.Status == PanelMultiFolderItemStatus.Added),
            items.Count(x => x.Status == PanelMultiFolderItemStatus.SkippedExisting),
            items.Count(x => x.Status == PanelMultiFolderItemStatus.Failed),
            items);
    }

    /// <summary>Concise bulk result using the application's Turkish notification wording.</summary>
    public static string BuildResultMessage(PanelMultiFolderBatchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var builder = new StringBuilder();
        builder.AppendLine($"{result.SelectedCount} klasör seçildi");
        builder.AppendLine();
        builder.AppendLine($"{result.AddedCount} klasör eklendi");
        if (result.SkippedExistingCount > 0) builder.AppendLine($"{result.SkippedExistingCount} klasör zaten mevcut");
        if (result.FailedCount > 0) builder.AppendLine($"{result.FailedCount} klasör eklenemedi");
        if (result.Items.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Detaylar:");
            foreach (var item in result.Items.Take(80))
            {
                builder.AppendLine(item.Status switch
                {
                    PanelMultiFolderItemStatus.Added => $"{item.FolderName}: EKLENDİ",
                    PanelMultiFolderItemStatus.SkippedExisting => $"{item.FolderName}: ZATEN MEVCUT",
                    _ => string.IsNullOrWhiteSpace(item.Error)
                        ? $"{item.FolderName}: EKLENEMEDİ"
                        : $"{item.FolderName}: EKLENEMEDİ — {item.Error}"
                });
            }
            if (result.Items.Count > 80) builder.AppendLine($"… {result.Items.Count - 80} öğe daha");
        }
        return builder.ToString().TrimEnd();
    }
}