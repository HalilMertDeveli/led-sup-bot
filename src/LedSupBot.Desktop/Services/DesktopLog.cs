using System.IO;

namespace LedSupBot.Desktop.Services;

public static class DesktopLog
{
    private static readonly object Sync = new();
    private static string? _logPath;

    private static string LogPath
    {
        get
        {
            if (_logPath is not null)
            {
                return _logPath;
            }

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LedSupport",
                "logs");
            Directory.CreateDirectory(dir);
            _logPath = Path.Combine(dir, "desktop.log");
            return _logPath;
        }
    }

    public static string FilePath => LogPath;

    public static void Info(string operation, string message) => Write("INFO", operation, message);

    public static void Warn(string operation, string message) => Write("WARN", operation, message);

    public static void Debug(string operation, string message) => Write("DEBUG", operation, message);

    public static void Error(string operation, Exception ex, int? httpStatus = null)
    {
        var status = httpStatus is null ? "" : $" status={httpStatus}";
        Write("ERROR", operation, $"{ex.GetType().Name}: {Sanitize(ex.Message)}{status}");
    }

    public static void Error(string operation, string message) => Write("ERROR", operation, message);

    private static void Write(string level, string operation, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} [{level}] {operation}: {message}";
        lock (Sync)
        {
            try
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch
            {
                /* logging must never crash the app */
            }
        }
    }

    private static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "unknown error";
        }

        var trimmed = message.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300] + "…";
    }
}
