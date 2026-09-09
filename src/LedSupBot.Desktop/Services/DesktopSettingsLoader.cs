using System.IO;
using System.Text.Json;
using LedSupBot.Desktop.Models;

namespace LedSupBot.Desktop.Services;

public static class DesktopSettingsLoader
{
    public static DesktopSettings Load()
    {
        var settings = new DesktopSettings();
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(path))
            {
                settings.ProjectRoot = ResolveProjectRoot() ?? "";
                return settings;
            }

            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<DesktopSettings>(json, JsonOptions);
            if (loaded is not null)
            {
                settings = loaded;
            }
        }
        catch
        {
            /* defaults */
        }

        if (string.IsNullOrWhiteSpace(settings.ProjectRoot))
        {
            settings.ProjectRoot = ResolveProjectRoot() ?? "";
        }

        return settings;
    }

    private static string? ResolveProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LedSupBot.sln")))
            {
                return dir.FullName;
            }

            if (Directory.Exists(Path.Combine(dir.FullName, "connector")) &&
                Directory.Exists(Path.Combine(dir.FullName, "src", "LedSupBot.Api")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
