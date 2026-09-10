namespace LedSupBot.UnitTests;

public sealed class PanelBulkImportUiWiringTests
{
    [Fact]
    public void Active_panel_files_view_exposes_visible_bulk_import_command()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "LedSupBot.Desktop", "MainWindow.xaml"));
        var panelView = File.ReadAllText(Path.Combine(root, "src", "LedSupBot.Desktop", "Views", "PanelFilesView.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "LedSupBot.Desktop", "ViewModels", "MainViewModel.cs"));
        var browserWindow = File.ReadAllText(Path.Combine(root, "src", "LedSupBot.Desktop", "Views", "PanelImportBrowserWindow.cs"));
        var previewWindow = File.ReadAllText(Path.Combine(root, "src", "LedSupBot.Desktop", "Views", "PanelBulkImportWindow.cs"));

        Assert.Contains("<views:PanelFilesView", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Content=\"TOPLU KLASÖR İÇE AKTAR\"", panelView, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding BulkImportPanelFoldersCommand}\"", panelView, StringComparison.Ordinal);
        Assert.Contains("public ICommand BulkImportPanelFoldersCommand { get; }", viewModel, StringComparison.Ordinal);
        Assert.Contains("BulkImportPanelFoldersCommand = new AsyncRelayCommand(BulkImportPanelFoldersAsync, () => !IsLoadingPanelPackages);",
            viewModel, StringComparison.Ordinal);
        Assert.Contains("new PanelImportBrowserWindow(dialog.FolderName)", viewModel, StringComparison.Ordinal);
        Assert.Contains("PanelImportSelectionNormalizer.Normalize(browser.SelectedPaths, existing)", viewModel, StringComparison.Ordinal);
        Assert.Contains("new PanelBulkImportWindow(plan)", viewModel, StringComparison.Ordinal);
        Assert.Contains("previewWindow.SelectedPackages", viewModel, StringComparison.Ordinal);
        Assert.Contains("SelectionMode = DataGridSelectionMode.Extended", browserWindow, StringComparison.Ordinal);
        Assert.Contains("Keyboard.Modifiers.HasFlag(ModifierKeys.Control)", browserWindow, StringComparison.Ordinal);
        Assert.Contains("_grid.SelectAll()", browserWindow, StringComparison.Ordinal);
        Assert.Contains("GERİ", browserWindow, StringComparison.Ordinal);
        Assert.Contains("YUKARI", browserWindow, StringComparison.Ordinal);
        Assert.Contains("YENİLE", browserWindow, StringComparison.Ordinal);
        Assert.Contains("SEÇİMİ TEMİZLE", browserWindow, StringComparison.Ordinal);
        Assert.Contains("ÖNİZLE", browserWindow, StringComparison.Ordinal);
        Assert.Contains("SEÇİLENLERİ İÇE AKTAR", previewWindow, StringComparison.Ordinal);
        Assert.Contains("plan.ImportablePackages", previewWindow, StringComparison.Ordinal);
        Assert.Contains("ImportPanelFilesAsync(item.FolderName, item.FilePaths)", viewModel, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LedSupBot.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root containing LedSupBot.sln was not found.");
    }
}
