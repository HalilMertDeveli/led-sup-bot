using LedSupBot.Application.PanelFiles;

namespace LedSupBot.UnitTests;

public sealed class PanelBulkFolderDiscoveryTests
{
    [Fact]
    public void Direct_children_are_previewed_independently_and_nested_folders_are_not_packages()
    {
        var root = Path.Combine(Path.GetTempPath(), "ledsup-bulk-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Make(root, "P2.5+1065S+2012", 3, nested: true);
            Make(root, "P1.86+1065+2018", 2);
            Make(root, "bad-folder", 4);
            Make(root, "P1.53+FM6126A+138", 1);
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "P2.5-1065S-2012" };

            var result = PanelBulkFolderDiscovery.Discover(root, existing);

            Assert.Equal(4, result.Count);
            Assert.Equal("REIMPORT", result.Single(x => x.FolderName == "P2.5+1065S+2012").Status);
            Assert.Equal("READY", result.Single(x => x.FolderName == "P1.86+1065+2018").Status);
            Assert.Equal(2, result.Count(x => x.Status == "INVALID"));
            Assert.DoesNotContain(result, x => x.FolderName == "nested");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void Make(string root, string name, int fileCount, bool nested = false)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
        for (var i = 0; i < fileCount; i++) File.WriteAllText(Path.Combine(folder, $"file-{i}.bin"), "safe");
        if (nested) Directory.CreateDirectory(Path.Combine(folder, "nested"));
    }
}
