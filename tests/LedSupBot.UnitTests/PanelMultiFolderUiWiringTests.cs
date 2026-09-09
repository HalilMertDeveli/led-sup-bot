namespace LedSupBot.UnitTests;

/// <summary>
/// Source-level wiring tests: the existing single-folder import flow must stay intact and the
/// new multi-folder action must reuse the SAME import pipeline with real cancel/empty guards.
/// </summary>
public sealed class PanelMultiFolderUiWiringTests
{
    [Fact] // TEST A — existing single-folder behavior unchanged
    public void Single_folder_import_flow_is_preserved()
    {
        var panelView = ReadSource("PanelFilesView.xaml");
        var viewModel = ReadSource("MainViewModel.cs");

        Assert.Contains("Content=\"KLASÖR İÇE AKTAR\"", panelView, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ImportPanelFolderCommand}\"", panelView, StringComparison.Ordinal);
        Assert.Contains("ImportPanelFolderCommand = new AsyncRelayCommand(ImportPanelFolderAsync, () => !IsLoadingPanelPackages);",
            viewModel, StringComparison.Ordinal);
        Assert.Contains("var imported = await _api.ImportPanelFolderAsync(dialog.FolderName);",
            viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void Multi_folder_action_exists_beside_single_folder_action()
    {
        var panelView = ReadSource("PanelFilesView.xaml");
        var viewModel = ReadSource("MainViewModel.cs");

        Assert.Contains("Content=\"BİRDEN FAZLA KLASÖR EKLE\"", panelView, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ImportMultiplePanelFoldersCommand}\"", panelView, StringComparison.Ordinal);
        Assert.Contains("public ICommand ImportMultiplePanelFoldersCommand { get; }", viewModel, StringComparison.Ordinal);
        Assert.Contains("ImportMultiplePanelFoldersCommand = new AsyncRelayCommand(ImportMultiplePanelFoldersAsync, () => !IsLoadingPanelPackages);",
            viewModel, StringComparison.Ordinal);
        // Both buttons coexist.
        Assert.Contains("Content=\"KLASÖR İÇE AKTAR\"", panelView, StringComparison.Ordinal);
    }

    [Fact]
    public void Multi_folder_flow_reuses_the_single_folder_pipeline()
    {
        var viewModel = ReadSource("MainViewModel.cs");

        // Same authoritative import call used by the single-folder flow, per selected folder.
        Assert.Contains("(folderPath, token) => _api.ImportPanelFolderAsync(folderPath, token)",
            viewModel, StringComparison.Ordinal);
        // Normalization + batch coordinator is shared/testable logic.
        Assert.Contains("PanelMultiFolderImport.RunAsync(", viewModel, StringComparison.Ordinal);
        // UI list is refreshed from the server after the batch (append semantics, no manual refresh).
        Assert.Contains("await LoadPanelPackagesAsync();", viewModel, StringComparison.Ordinal);
        // Bulk result summary is surfaced.
        Assert.Contains("PanelMultiFolderImport.BuildResultMessage(result)", viewModel, StringComparison.Ordinal);
    }

    [Fact] // TEST I — cancel makes zero changes
    public void Multi_folder_flow_aborts_before_any_import_on_cancel_or_empty_selection()
    {
        var viewModel = ReadSource("MainViewModel.cs");

        Assert.Contains("confirmed = picker.ShowDialog() == true;", viewModel, StringComparison.Ordinal);
        Assert.Contains("if (!confirmed) return;", viewModel, StringComparison.Ordinal);
        Assert.Contains("if (selectedFolders.Count == 0) return;", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void Multi_folder_picker_uses_true_extended_multi_selection()
    {
        var picker = ReadSource("PanelMultiFolderSelectionWindow.cs");

        // Native WPF extended selection: SHIFT range + CTRL toggle + CTRL+A.
        Assert.Contains("SelectionMode = SelectionMode.Extended", picker, StringComparison.Ordinal);
        Assert.Contains("_list.SelectAll()", picker, StringComparison.Ordinal);
        // Folder-only directory listing (explicitly selected folders only).
        Assert.Contains("Directory.EnumerateDirectories", picker, StringComparison.Ordinal);
        // Selections are retained while navigating.
        Assert.Contains("_selectedPaths = new(StringComparer.OrdinalIgnoreCase)", picker, StringComparison.Ordinal);
        // Confirm returns the selection; cancel keeps state untouched.
        Assert.Contains("DialogResult = true;", picker, StringComparison.Ordinal);
        Assert.Contains("\"İPTAL\"", picker, StringComparison.Ordinal);
        Assert.Contains("\"SEÇ\"", picker, StringComparison.Ordinal);
        Assert.Contains("\"SEÇİMİ TEMİZLE\"", picker, StringComparison.Ordinal);
        // Explorer-like navigation.
        Assert.Contains("\"GERİ\"", picker, StringComparison.Ordinal);
        Assert.Contains("\"YUKARI\"", picker, StringComparison.Ordinal);
        Assert.Contains("\"YOLA GİT\"", picker, StringComparison.Ordinal);
    }

    private static string ReadSource(string fileName)
    {
        var root = FindRepositoryRoot();
        var relative = fileName.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine("src", "LedSupBot.Desktop", "Views", fileName)
            : Path.Combine("src", "LedSupBot.Desktop", "Views", fileName);
        if (!File.Exists(Path.Combine(root, relative)) && fileName == "MainViewModel.cs")
        {
            relative = Path.Combine("src", "LedSupBot.Desktop", "ViewModels", fileName);
        }
        return File.ReadAllText(Path.Combine(root, relative));
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