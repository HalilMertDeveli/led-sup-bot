using LedSupBot.Application.Media;

namespace LedSupBot.UnitTests;

public sealed class RuntimeMediaStorageRootTests
{
    [Fact]
    public void DesktopApiLaunch_ReusesExistingApiProjectMediaRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ledsup-runtime-{Guid.NewGuid():N}");
        var mediaRoot = Path.Combine(root, "src", "LedSupBot.Api", "data", "media");
        Directory.CreateDirectory(mediaRoot);
        try
        {
            Assert.Equal(Path.GetFullPath(mediaRoot), MediaStorageRootResolver.FindExistingProjectStore(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
