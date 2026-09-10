namespace LedSupBot.Application.Media;

public static class MediaStorageRootResolver
{
    public static string? FindExistingProjectStore(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot)) return null;

        var candidate = Path.GetFullPath(Path.Combine(projectRoot, "src", "LedSupBot.Api", "data", "media"));
        return Directory.Exists(candidate) ? candidate : null;
    }
}
