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
            }
            else
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<DesktopSettings>(json, JsonOptions);
                if (loaded is not null)
                {
                    settings = loaded;
                }
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

        if (string.IsNullOrWhiteSpace(settings.RuntimeRoot))
        {
            settings.RuntimeRoot = ResolveRuntimeRoot(settings.ProjectRoot);
        }

        return settings;
    }

    public static string ResolveRuntimeRoot(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return "";
        }

        var gitFile = Path.Combine(projectRoot, ".git");
        if (!File.Exists(gitFile))
        {
            return projectRoot;
        }

        try
        {
            const string prefix = "gitdir:";
            var line = File.ReadLines(gitFile).FirstOrDefault()?.Trim();
            if (line is null || !line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return projectRoot;
            }

            var gitDirectory = line[prefix.Length..].Trim();
            if (!Path.IsPathRooted(gitDirectory))
            {
                gitDirectory = Path.GetFullPath(Path.Combine(projectRoot, gitDirectory));
            }

            var mainGitDirectory = Directory.GetParent(gitDirectory)?.Parent;
            var mainRoot = mainGitDirectory?.Parent?.FullName;
            if (!string.IsNullOrWhiteSpace(mainRoot) &&
                File.Exists(Path.Combine(mainRoot, LocalRuntimeConfigLoader.FileName)) &&
                Directory.Exists(Path.Combine(mainRoot, "connector")))
            {
                return mainRoot;
            }
        }
        catch (IOException)
        {
            // Fall back to the current checkout when Git metadata cannot be read.
        }
        catch (UnauthorizedAccessException)
        {
            // Fall back to the current checkout when Git metadata cannot be read.
        }

        return projectRoot;
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
