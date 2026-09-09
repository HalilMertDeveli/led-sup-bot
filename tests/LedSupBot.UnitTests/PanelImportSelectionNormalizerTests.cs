using LedSupBot.Application.PanelFiles;

namespace LedSupBot.UnitTests;

public sealed class PanelImportSelectionNormalizerTests
{
    [Fact]
    public void Multiple_selected_folders_become_independent_packages()
    {
        using var scope = new TempPackages();
        var first = scope.Package("P1.86+1065+2018", "a.hex", "b.rcvbp");
        var second = scope.Package("P2.5+1065S+2012", "c.hex", "d.rcvbp");

        var plan = PanelImportSelectionNormalizer.Normalize([first, second]);

        Assert.Equal(2, plan.ImportablePackages.Count);
        Assert.Contains(plan.Packages, x => x.CanonicalKey == "P1.86-1065-2018");
        Assert.Contains(plan.Packages, x => x.CanonicalKey == "P2.5-1065S-2012");
    }

    [Fact]
    public void Folder_and_files_can_coexist_and_file_uses_immediate_parent_identity()
    {
        using var scope = new TempPackages();
        var folder = scope.Package("P1.86+1065+2018", "a.hex", "b.rcvbp");
        var other = scope.Package("P3.33+3265S+32020", "scan.rcvbp", "extra.bin");

        var plan = PanelImportSelectionNormalizer.Normalize([folder, Path.Combine(other, "scan.rcvbp"), Path.Combine(other, "extra.bin")]);

        Assert.Equal(2, plan.ImportablePackages.Count);
        var selectedFiles = Assert.Single(plan.Packages, x => x.CanonicalKey == "P3.33-3265S-32020");
        Assert.Equal(2, selectedFiles.FilePaths.Count);
        Assert.Equal(2, selectedFiles.SelectedPaths.Count);
    }

    [Fact]
    public void Loose_file_with_invalid_parent_is_rejected_without_filename_inference()
    {
        using var scope = new TempPackages();
        var loose = scope.Package("Downloads", "P2.5+1065S+2012.hex", "other.rcvbp");

        var plan = PanelImportSelectionNormalizer.Normalize([Path.Combine(loose, "P2.5+1065S+2012.hex")]);

        var item = Assert.Single(plan.Packages);
        Assert.False(item.CanImport);
        Assert.Equal("NO PACKAGE", item.Status);
        Assert.Null(item.CanonicalKey);
        Assert.Equal("PACKAGE IDENTITY REQUIRED", item.Error);
    }

    [Fact]
    public void Selected_folder_and_same_child_file_are_path_deduplicated()
    {
        using var scope = new TempPackages();
        var folder = scope.Package("P2.5+1065S+2012", "first.hex", "second.rcvbp", "third.bin");

        var plan = PanelImportSelectionNormalizer.Normalize([folder, Path.Combine(folder, "first.hex")]);

        var package = Assert.Single(plan.ImportablePackages);
        Assert.Equal(3, package.FilePaths.Count);
        Assert.Equal(3, package.FilePaths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Only_explicitly_selected_files_are_included()
    {
        using var scope = new TempPackages();
        var folder = scope.Package("P2.5+1065S+2012", "first.hex", "second.rcvbp", "not-selected.bin");

        var plan = PanelImportSelectionNormalizer.Normalize(
            [Path.Combine(folder, "first.hex"), Path.Combine(folder, "second.rcvbp")]);

        var package = Assert.Single(plan.ImportablePackages);
        Assert.Equal(2, package.FilePaths.Count);
        Assert.DoesNotContain(package.FilePaths, x => Path.GetFileName(x) == "not-selected.bin");
    }

    [Fact]
    public void One_selected_file_cannot_create_an_accidental_package()
    {
        using var scope = new TempPackages();
        var folder = scope.Package("P2.5+1065S+2012", "only.hex");

        var item = Assert.Single(PanelImportSelectionNormalizer.Normalize([Path.Combine(folder, "only.hex")]).Packages);

        Assert.False(item.CanImport);
        Assert.Equal("INVALID", item.Status);
        Assert.Contains("En az 2", item.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Arbitrary_number_of_regular_files_is_preserved()
    {
        using var scope = new TempPackages();
        var folder = scope.Package("P2.5+1065S+2012", Enumerable.Range(1, 7).Select(x => $"asset-{x}.bin").ToArray());

        var package = Assert.Single(PanelImportSelectionNormalizer.Normalize([folder]).ImportablePackages);

        Assert.Equal(7, package.FilePaths.Count);
    }

    [Fact]
    public void Existing_package_is_marked_for_reimport()
    {
        using var scope = new TempPackages();
        var folder = scope.Package("P2.5+1065S+2012", "first.hex", "second.rcvbp");
        var existing = new HashSet<string>(["P2.5-1065S-2012"], StringComparer.OrdinalIgnoreCase);

        var package = Assert.Single(PanelImportSelectionNormalizer.Normalize([folder], existing).ImportablePackages);

        Assert.Equal("REIMPORT", package.Status);
    }

    private sealed class TempPackages : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ledsup-selection-" + Guid.NewGuid().ToString("N"));

        public TempPackages() => Directory.CreateDirectory(_root);

        public string Package(string name, params string[] fileNames)
        {
            var directory = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
            foreach (var fileName in fileNames) File.WriteAllText(Path.Combine(directory, fileName), fileName);
            return directory;
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
