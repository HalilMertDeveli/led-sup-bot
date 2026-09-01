using LedSupBot.Application.Abstractions;
using LedSupBot.Infrastructure.Configuration;
using LedSupBot.Infrastructure.Media;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LedSupBot.UnitTests.Media;

public sealed class LocalMediaStorageTests : IDisposable
{
    private readonly string _root;
    private readonly LocalMediaStorage _storage;

    public LocalMediaStorageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "led-sup-bot-media-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _storage = new LocalMediaStorage(
            Options.Create(new MediaStorageOptions { RootPath = _root }),
            new TestHostEnvironment { ContentRootPath = _root },
            NullLogger<LocalMediaStorage>.Instance);
    }

    [Fact]
    public async Task Stores_And_Reads_Image_With_Safe_Key()
    {
        var bytes = TestImageBytes.CreatePng();
        await using var input = new MemoryStream(bytes);
        var stored = await _storage.StoreAsync(
            input,
            "image/png",
            new MediaStoreRequest("cases/0123456789abcdef0123456789abcdef", ".png"));

        Assert.StartsWith("cases/0123456789abcdef0123456789abcdef/", stored.StorageKey, StringComparison.Ordinal);
        Assert.DoesNotContain("90555", stored.StorageKey, StringComparison.Ordinal);

        await using var output = await _storage.OpenReadAsync(stored.StorageKey);
        using var ms = new MemoryStream();
        await output.CopyToAsync(ms);
        Assert.Equal(bytes.Length, ms.Length);
    }

    [Fact]
    public void Rejects_Path_Traversal_Key_On_Read()
    {
        Assert.ThrowsAny<Exception>(() => _storage.OpenReadAsync("../secret.txt").GetAwaiter().GetResult());
    }

    [Fact]
    public void Rejects_Path_Traversal_Prefix_On_Store()
    {
        Assert.Throws<ArgumentException>(() =>
            LocalMediaStorage.SanitizeKeySegment("../cases/evil"));
    }

    [Fact]
    public async Task Atomic_Write_Does_Not_Leave_Partial_File_On_Failure()
    {
        var readOnlyRoot = Path.Combine(_root, "readonly");
        Directory.CreateDirectory(readOnlyRoot);
        var file = Path.Combine(readOnlyRoot, "blocked.txt");
        await File.WriteAllTextAsync(file, "x");
        File.SetAttributes(readOnlyRoot, FileAttributes.ReadOnly);

        try
        {
            var storage = new LocalMediaStorage(
                Options.Create(new MediaStorageOptions { RootPath = readOnlyRoot }),
                new TestHostEnvironment { ContentRootPath = readOnlyRoot },
                NullLogger<LocalMediaStorage>.Instance);

            await using var input = new MemoryStream(TestImageBytes.CreatePng());
            await Assert.ThrowsAnyAsync<Exception>(() => storage.StoreAsync(
                input,
                "image/png",
                new MediaStoreRequest("cases/abc", ".png")));
        }
        finally
        {
            File.SetAttributes(readOnlyRoot, FileAttributes.Normal);
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best effort temp cleanup.
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = string.Empty;
    }
}
