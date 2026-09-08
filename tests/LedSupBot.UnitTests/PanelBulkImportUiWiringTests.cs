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

        Assert.Contains("<views:PanelFilesView", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Content=\"TOPLU KLASÖR İÇE AKTAR\"", panelView, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding BulkImportPanelFoldersCommand}\"", panelView, StringComparison.Ordinal);
        Assert.Contains("public ICommand BulkImportPanelFoldersCommand { get; }", viewModel, StringComparison.Ordinal);
        Assert.Contains("BulkImportPanelFoldersCommand = new AsyncRelayCommand(BulkImportPanelFoldersAsync, () => !IsLoadingPanelPackages);",
            viewModel, StringComparison.Ordinal);
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
