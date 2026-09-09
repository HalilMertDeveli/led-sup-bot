using LedSupBot.Application.PanelFiles;

namespace LedSupBot.UnitTests;

/// <summary>
/// Targeted logic tests for the multi-folder add pipeline (PanelMultiFolderImport).
/// The import delegate stands in for the existing single-folder import call
/// (LedSupBotApiClient.ImportPanelFolderAsync) so every folder selected in the
/// multi-folder dialog is verified to flow through the same pipeline.
/// </summary>
public sealed class PanelMultiFolderImportTests
{
    private static readonly Func<string, CancellationToken, Task> OkImport = (_, _) => Task.CompletedTask;

    private static Func<string, CancellationToken, Task> RecordingImport(ICollection<string> imported,
        params (string PathPart, Exception Error)[] failures)
    {
        return (folder, _) =>
        {
            var failure = failures.FirstOrDefault(f => folder.Contains(f.PathPart, StringComparison.OrdinalIgnoreCase));
            if (failure.Error is not null) throw failure.Error;
            imported.Add(folder);
            return Task.CompletedTask;
        };
    }

    [Fact] // TEST B
    public async Task Three_valid_folders_are_all_added_through_single_folder_pipeline()
    {
        var imported = new List<string>();
        var folders = new[]
        {
            @"E:\Specs\P2.5+HX3815+ICN2153",
            @"E:\Specs\P2.8+AA11+BB22",
            @"E:\Specs\P3.9+CC33+DD44"
        };

        var result = await PanelMultiFolderImport.RunAsync(folders, null, RecordingImport(imported));

        Assert.Equal(3, result.SelectedCount);
        Assert.Equal(3, result.AddedCount);
        Assert.Equal(0, result.SkippedExistingCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(3, imported.Count);
        Assert.All(result.Items, item => Assert.Equal(PanelMultiFolderItemStatus.Added, item.Status));
    }

    [Fact] // TEST C
    public async Task Already_configured_folder_is_skipped_and_new_folder_is_added()
    {
        var imported = new List<string>();
        var folders = new[] { @"E:\Specs\P2.5+HX3815+ICN2153", @"E:\Specs\P3.1+XX99+YY88" };
        var existing = new[] { "P2.5-HX3815-ICN2153" };

        var result = await PanelMultiFolderImport.RunAsync(folders, existing, RecordingImport(imported));

        Assert.Equal(2, result.SelectedCount);
        Assert.Equal(1, result.AddedCount);
        Assert.Equal(1, result.SkippedExistingCount);
        Assert.Equal(0, result.FailedCount);
        var skipped = Assert.Single(result.Items, x => x.Status == PanelMultiFolderItemStatus.SkippedExisting);
        Assert.Equal(@"E:\Specs\P2.5+HX3815+ICN2153", skipped.FolderPath);
        // The single-folder pipeline must NOT run for the already-configured folder.
        Assert.Equal(@"E:\Specs\P3.1+XX99+YY88", Assert.Single(imported));
    }

    [Fact] // TEST D
    public async Task Duplicate_paths_inside_one_batch_are_processed_once()
    {
        var imported = new List<string>();
        var folders = new[]
        {
            @"E:\Specs\P2.5+HX3815+ICN2153",
            @"E:\Specs\P2.5+HX3815+ICN2153\",
            @"e:\SPECS\p2.5+hx3815+icn2153"
        };

        var result = await PanelMultiFolderImport.RunAsync(folders, null, RecordingImport(imported));

        Assert.Equal(1, result.SelectedCount);
        Assert.Equal(1, result.AddedCount);
        Assert.Single(imported);
        Assert.Single(result.Items);
    }

    [Fact] // TEST E
    public async Task Valid_folders_are_added_when_another_selected_folder_is_invalid()
    {
        var imported = new List<string>();
        var folders = new[]
        {
            @"E:\Specs\P2.5+HX3815+ICN2153",
            @"E:\Specs\Invalid_Folder_Name",
            @"E:\Specs\P2.8+AA11+BB22"
        };

        var result = await PanelMultiFolderImport.RunAsync(folders, null, RecordingImport(imported,
            ("Invalid_Folder_Name", new InvalidDataException("Bu klasör adı beklenen P+Chipset+Decoder formatında değil."))));

        Assert.Equal(3, result.SelectedCount);
        Assert.Equal(2, result.AddedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(2, imported.Count);
        var failed = Assert.Single(result.Items, x => x.Status == PanelMultiFolderItemStatus.Failed);
        Assert.Contains("P+Chipset+Decoder", failed.Error);
    }

    [Fact] // TEST F
    public async Task Trailing_separator_and_casing_represent_the_same_folder()
    {
        var imported = new List<string>();
        var folders = new[]
        {
            @"E:\Specs\Receivers\",
            @"E:\Specs\Receivers",
            @"E:\SPECS\RECEIVERS"
        };

        var result = await PanelMultiFolderImport.RunAsync(folders, null, RecordingImport(imported));

        Assert.Equal(1, result.SelectedCount);
        Assert.Equal(1, result.AddedCount);
        Assert.Single(imported);
    }

    [Fact] // TEST F (unit)
    public void NormalizeFolderPath_trims_trailing_separators_and_keeps_drive_root()
    {
        Assert.Equal(@"E:\Specs\Receivers", PanelMultiFolderImport.NormalizeFolderPath(@"E:\Specs\Receivers\"));
        Assert.Equal(@"E:\Specs\Receivers", PanelMultiFolderImport.NormalizeFolderPath(@"E:\Specs\Receivers/"));
        Assert.Equal(@"E:\", PanelMultiFolderImport.NormalizeFolderPath(@"E:\"));
        Assert.Equal(@"E:\", PanelMultiFolderImport.NormalizeFolderPath(@"E:/"));
    }

    [Fact] // TEST G
    public async Task Every_selected_folder_is_reported_so_existing_entries_are_only_appended()
    {
        var existing = new[] { "P2.5-HX3815-ICN2153" };
        var folders = new[]
        {
            @"E:\Specs\P2.5+HX3815+ICN2153", // already configured
            @"E:\Specs\P2.8+AA11+BB22",      // new
            @"E:\Specs\Broken_Folder"        // invalid
        };

        var result = await PanelMultiFolderImport.RunAsync(folders, existing, RecordingImport(new List<string>(),
            ("Broken_Folder", new InvalidDataException("Bu klasör adı beklenen P+Chipset+Decoder formatında değil."))));

        // Nothing is dropped or replaced: skipped + added + failed all stay in the result,
        // so callers can only ever append to the existing configured collection.
        Assert.Equal(3, result.SelectedCount);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(1, result.AddedCount);
        Assert.Equal(1, result.SkippedExistingCount);
        Assert.Equal(1, result.FailedCount);
    }

    [Fact] // TEST H
    public async Task Empty_selection_changes_nothing()
    {
        var called = false;

        var result = await PanelMultiFolderImport.RunAsync(
            Array.Empty<string>(), null, (_, _) => { called = true; return Task.CompletedTask; });

        Assert.Equal(0, result.SelectedCount);
        Assert.Equal(0, result.AddedCount);
        Assert.Empty(result.Items);
        Assert.False(called);
    }

    [Fact] // TEST J
    public async Task One_folder_access_failure_does_not_block_remaining_folders()
    {
        var imported = new List<string>();
        var folders = new[]
        {
            @"E:\Specs\P2.5+HX3815+ICN2153",
            @"E:\Specs\Locked_Folder_Name",
            @"E:\Specs\P2.8+AA11+BB22",
            @"E:\Specs\P3.9+CC33+DD44"
        };

        var result = await PanelMultiFolderImport.RunAsync(folders, null, RecordingImport(imported,
            ("Locked_Folder_Name", new UnauthorizedAccessException("Erişim reddedildi."))));

        Assert.Equal(4, result.SelectedCount);
        Assert.Equal(3, result.AddedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(3, imported.Count);
        // Items keep the input order so the batch remains deterministic.
        Assert.Equal(folders, result.Items.Select(x => x.FolderPath).ToArray());
    }

    [Fact]
    public void BuildResultMessage_reports_selected_added_skipped_and_failed_counts()
    {
        var result = new PanelMultiFolderBatchResult(5, 3, 1, 1,
        [
            new(@"E:\Specs\A", "A", PanelMultiFolderItemStatus.Added),
            new(@"E:\Specs\B", "B", PanelMultiFolderItemStatus.Added),
            new(@"E:\Specs\C", "C", PanelMultiFolderItemStatus.Added),
            new(@"E:\Specs\D", "D", PanelMultiFolderItemStatus.SkippedExisting),
            new(@"E:\Specs\E", "E", PanelMultiFolderItemStatus.Failed, "Erişim reddedildi.")
        ]);

        var message = PanelMultiFolderImport.BuildResultMessage(result);

        Assert.Contains("5 klasör seçildi", message, StringComparison.Ordinal);
        Assert.Contains("3 klasör eklendi", message, StringComparison.Ordinal);
        Assert.Contains("1 klasör zaten mevcut", message, StringComparison.Ordinal);
        Assert.Contains("1 klasör eklenemedi", message, StringComparison.Ordinal);
        Assert.Contains("D: ZATEN MEVCUT", message, StringComparison.Ordinal);
        Assert.Contains("E: EKLENEMEDİ — Erişim reddedildi.", message, StringComparison.Ordinal);
    }
}