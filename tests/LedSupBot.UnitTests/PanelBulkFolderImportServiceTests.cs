using LedSupBot.Application.PanelFiles;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.PanelFiles;
using Microsoft.Extensions.Logging.Abstractions;

namespace LedSupBot.UnitTests;

public sealed class PanelBulkFolderImportServiceTests
{
    [Fact]
    public async Task BULK_01_03_06_09_10_valid_parent_discovers_and_imports_each_package_independently()
    {
        var root = Path.Combine(Path.GetTempPath(), "ledsup-bulk-import", Guid.NewGuid().ToString("N"));
        var importer = new CountingFolderImporter();
        var service = new LedSupBot.Infrastructure.PanelFiles.PanelBulkFolderImportService(
            importer, NullLogger<LedSupBot.Infrastructure.PanelFiles.PanelBulkFolderImportService>.Instance);

        try
        {
            Make(root, "P2.5+1065S+2012", 3);
            Make(root, "P1.86+1065+2018", 2);
            Make(root, "P3.076+1065AP+1012", 3);
            Make(root, "bad-folder", 4);

            var result = await service.ImportAllAsync(root);

            Assert.Equal(4, result.Found);
            Assert.Equal(3, result.Imported);
            Assert.Equal(1, result.Skipped);
            Assert.Equal(0, result.Failed);
            Assert.Equal(3, importer.ImportedCount);
            Assert.Contains(result.Results, x => x.Status == "SkippedInvalid" && x.FolderName == "bad-folder");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BULK_02_04_all_files_of_each_package_passed_including_arbitrary_names()
    {
        var root = Path.Combine(Path.GetTempPath(), "ledsup-bulk-import-files", Guid.NewGuid().ToString("N"));
        var importer = new RecordingFolderImporter();
        var service = new LedSupBot.Infrastructure.PanelFiles.PanelBulkFolderImportService(
            importer, NullLogger<LedSupBot.Infrastructure.PanelFiles.PanelBulkFolderImportService>.Instance);

        try
        {
            MakeFiles(root, "P2.5+1065S+2012", ["abc.hex", "random.rcvbp", "another.bin"]);
            MakeFiles(root, "invalid", ["a.txt"]);

            _ = await service.ImportAllAsync(root);

            Assert.Equal(1, importer.Calls.Count);
            var import = importer.Calls.Single();
            Assert.Equal("P2.5+1065S+2012", import.FolderName);
            Assert.Equal(3, import.Files.Count);
            Assert.Contains(import.Files, f => f.FileName == "abc.hex");
            Assert.Contains(import.Files, f => f.FileName == "random.rcvbp");
            Assert.Contains(import.Files, f => f.FileName == "another.bin");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BULK_05_reuses_authoritative_single_import_service_contract()
    {
        var root = Path.Combine(Path.GetTempPath(), "ledsup-bulk-import-same", Guid.NewGuid().ToString("N"));
        var importer = new RecordingFolderImporter();
        var service = new LedSupBot.Infrastructure.PanelFiles.PanelBulkFolderImportService(
            importer, NullLogger<LedSupBot.Infrastructure.PanelFiles.PanelBulkFolderImportService>.Instance);

        try
        {
            Make(root, "P2.5+1065S+2012", 2);
            _ = await service.ImportAllAsync(root);

            // Bulk service MUST invoke the exact single-import authoritative contract.
            Assert.True(importer.Calls.Single().Files.Count >= 2);
            Assert.Same(typeof(LedSupBot.Infrastructure.PanelFiles.PanelFolderImportService), importer.DelegateType);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BULK_07_one_package_failure_does_not_corrupt_another()
    {
        var root = Path.Combine(Path.GetTempPath(), "ledsup-bulk-import-fail", Guid.NewGuid().ToString("N"));
        var importer = new FailingFolderImporter { FailOnFolder = "P1.86+1065+2018" };
        var service = new LedSupBot.Infrastructure.PanelFiles.PanelBulkFolderImportService(
            importer, NullLogger<LedSupBot.Infrastructure.PanelFiles.PanelBulkFolderImportService>.Instance);

        try
        {
            Make(root, "P2.5+1065S+2012", 2);
            Make(root, "P1.86+1065+2018", 2);
            Make(root, "P3.076+1065AP+1012", 2);

            var result = await service.ImportAllAsync(root);

            Assert.Equal(2, result.Imported);
            Assert.Equal(1, result.Failed);
            Assert.Equal(2, result.Results.Count(x => x.Status == "Imported"));
            Assert.Equal("Failed", result.Results.Single(x => x.FolderName == "P1.86+1065+2018").Status);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PanelBulkFolderDiscovery_Requires_Two_Or_More_Files_And_No_Nested_Packages()
    {
        var root = Path.Combine(Path.GetTempPath(), "ledsup-bulk-discovery2", Guid.NewGuid().ToString("N"));
        try
        {
            Make(root, "P2.5+1065S+2012", 3, nested: true);
            Make(root, "P1.86+1065+2018", 1);

            var candidates = PanelBulkFolderDiscovery.Discover(root);
            Assert.Equal(2, candidates.Count);
            Assert.Equal("INVALID", candidates.Single(x => x.FolderName == "P1.86+1065+2018").Status);
            Assert.Equal("READY", candidates.Single(x => x.FolderName == "P2.5+1065S+2012").Status);
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
        if (nested) Directory.CreateDirectory(Path.Combine(folder, "nested-child"));
    }

    private static void MakeFiles(string root, string name, List<string> fileNames)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
        foreach (var fileName in fileNames) File.WriteAllText(Path.Combine(folder, fileName), "data");
    }

    private sealed class CountingFolderImporter : IPanelFolderImportService
    {
        public int ImportedCount;
        public List<int> LastFileCounts { get; } = [];

        public async Task<PanelFilePackage> ImportAsync(string folderName, IReadOnlyList<PanelFolderImportFile> files,
            CancellationToken cancellationToken = default)
        {
            ImportedCount++;
            LastFileCounts.Add(files.Count);
            return new PanelFilePackage { CanonicalKey = folderName.Replace('+', '-') };
        }
    }

    private sealed class RecordingFolderImporter : IPanelFolderImportService
    {
        public List<(string FolderName, IReadOnlyList<PanelFolderImportFile> Files)> Calls { get; } = [];
        public Type DelegateType => typeof(LedSupBot.Infrastructure.PanelFiles.PanelFolderImportService);

        public async Task<PanelFilePackage> ImportAsync(string folderName, IReadOnlyList<PanelFolderImportFile> files,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((folderName, files));
            return new PanelFilePackage { CanonicalKey = folderName.Replace('+', '-') };
        }
    }

    private sealed class FailingFolderImporter : IPanelFolderImportService
    {
        public required string FailOnFolder { get; set; }

        public async Task<PanelFilePackage> ImportAsync(string folderName, IReadOnlyList<PanelFolderImportFile> files,
            CancellationToken cancellationToken = default)
        {
            if (string.Equals(folderName, FailOnFolder, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("simulated import failure");
            }

            return new PanelFilePackage { CanonicalKey = folderName.Replace('+', '-') };
        }
    }
}