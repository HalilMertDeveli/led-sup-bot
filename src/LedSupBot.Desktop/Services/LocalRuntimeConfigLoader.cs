using System.IO;
using System.Text.Json;

namespace LedSupBot.Desktop.Services;

public static class LocalRuntimeConfigLoader
{
    public const string FileName = "appsettings.Local.json";

    public static void MergeInto(Dictionary<string, string> target, string projectRoot, bool overwrite = true)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return;
        }

        var path = Path.Combine(projectRoot, FileName);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            SetMapped(target, overwrite, "DATABASE_CONNECTION_STRING", root, "Database", "ConnectionString");
            SetMapped(target, overwrite, "OPENAI_API_KEY", root, "OpenAI", "ApiKey");
            SetMapped(target, overwrite, "OPENAI_MODEL", root, "OpenAI", "Model");
            SetMapped(target, overwrite, "AI_PROVIDER", root, "AI", "Provider");
            SetMapped(target, overwrite, "GEMINI_API_KEY", root, "Gemini", "ApiKey");
            SetMapped(target, overwrite, "GEMINI_MODEL", root, "Gemini", "Model");
            SetMapped(target, overwrite, "WHATSAPP_CONNECTOR_SHARED_SECRET", root, "WhatsAppConnector", "SharedSecret");
            SetMapped(target, overwrite, "CONNECTOR_SHARED_SECRET", root, "WhatsAppConnector", "SharedSecret");
            SetMapped(target, overwrite, "WHATSAPP_CONNECTOR_ENABLED", root, "WhatsAppConnector", "Enabled");
            SetMapped(target, overwrite, "WHATSAPP_CONNECTOR_BASE_URL", root, "WhatsAppConnector", "BaseUrl");
            SetMapped(target, overwrite, "WORKFLOW_AUTO_REPLY_ENABLED", root, "Workflow", "AutoReplyEnabled");
            SetMapped(target, overwrite, "WORKFLOW_CONFIG_EXAMPLE_IMAGE_PATH", root, "Workflow", "ConfigExampleImagePath");
            SetMapped(target, overwrite, "WHATSAPP_TEST_MODE_ENABLED", root, "WhatsAppTestMode", "Enabled");
            SetMapped(target, overwrite, "WHATSAPP_TEST_MODE_ALLOWED_PHONE", root, "WhatsAppTestMode", "AllowedPhoneNumber");
            SetMapped(target, overwrite, "ADMIN_DASHBOARD_PASSWORD", root, "Admin", "DashboardPassword");
            SetMapped(target, overwrite, "ADMIN_SESSION_SECRET", root, "Admin", "SessionSecret");
        }
        catch (Exception ex)
        {
            DesktopLog.Error("LocalRuntimeConfig", ex);
        }
    }

    public static string? GetAdminDashboardPassword(string? projectRoot)
    {
        var envPass = Environment.GetEnvironmentVariable("ADMIN_DASHBOARD_PASSWORD");
        if (!string.IsNullOrWhiteSpace(envPass))
        {
            return envPass;
        }

        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return null;
        }

        var path = Path.Combine(projectRoot, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("Admin", out var admin) &&
                admin.TryGetProperty("DashboardPassword", out var prop))
            {
                var val = prop.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return val;
                }
            }
        }
        catch
        {
            /* best effort */
        }

        return null;
    }

    public static void EnsureTemplateExists(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return;
        }

        var path = GetLocalConfigPath(projectRoot);
        if (File.Exists(path))
        {
            return;
        }

        var example = Path.Combine(projectRoot, "appsettings.Local.example.json");
        if (File.Exists(example))
        {
            File.Copy(example, path);
        }
    }

    public static bool FileExists(string projectRoot) =>
        !string.IsNullOrWhiteSpace(projectRoot) && File.Exists(GetLocalConfigPath(projectRoot));

    private static void SetMapped(
        Dictionary<string, string> target,
        bool overwrite,
        string envKey,
        JsonElement root,
        string section,
        string property)
    {
        if (!TryReadString(root, section, property, out var value))
        {
            return;
        }

        if (overwrite || !target.ContainsKey(envKey))
        {
            target[envKey] = value;
        }
    }

    public static string GetLocalConfigPath(string projectRoot) =>
        Path.Combine(projectRoot, FileName);

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryReadString(JsonElement root, string section, string property, out string value)
    {
        value = "";
        if (!TryGetProperty(root, section, out var sectionEl))
        {
            return false;
        }

        if (!TryGetProperty(sectionEl, property, out var propEl))
        {
            return false;
        }

        value = propEl.ValueKind switch
        {
            JsonValueKind.String => propEl.GetString()?.Trim() ?? "",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => propEl.GetRawText(),
            _ => "",
        };

        return !string.IsNullOrWhiteSpace(value);
    }
}
