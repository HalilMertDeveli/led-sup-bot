using System.IO;

namespace LedSupBot.Application.Workflow;

public static class ConfigAssetResolver
{
    private const string DefaultRelativeSubPath = "assets/support/usb-a-usb-b-printer-cable.jpg";

    public static string? ResolvePath(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var candidates = new[]
        {
            configuredPath,
            Path.Combine("E:\\led-sup-bot", "assets", "support", "usb-a-usb-b-printer-cable.jpg"),
            Path.Combine("E:\\led-sup-bot", "assets", "support", "usb-a-usb-b-printer-cable.jpg.png"),
            Path.Combine(AppContext.BaseDirectory, "assets", "support", "usb-a-usb-b-printer-cable.jpg"),
            Path.Combine(AppContext.BaseDirectory, "..", "assets", "support", "usb-a-usb-b-printer-cable.jpg"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "assets", "support", "usb-a-usb-b-printer-cable.jpg"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "assets", "support", "usb-a-usb-b-printer-cable.jpg"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "assets", "support", "usb-a-usb-b-printer-cable.jpg"),
            Path.Combine(Directory.GetCurrentDirectory(), "assets", "support", "usb-a-usb-b-printer-cable.jpg"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "assets", "support", "usb-a-usb-b-printer-cable.jpg"),
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static bool IsAssetAvailable(string? configuredPath = null) =>
        !string.IsNullOrWhiteSpace(ResolvePath(configuredPath));
}
