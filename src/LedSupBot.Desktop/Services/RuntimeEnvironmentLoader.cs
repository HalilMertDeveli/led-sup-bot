using System.IO;
using LedSupBot.Desktop.Models;

namespace LedSupBot.Desktop.Services;

public sealed class RuntimeEnvironmentLoader
{
    private readonly Dictionary<string, string> _config = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _projectRoot;

    public RuntimeEnvironmentLoader(DesktopSettings settings)
    {
        _projectRoot = settings.ProjectRoot;

        if (string.IsNullOrWhiteSpace(_projectRoot))
        {
            return;
        }

        LocalRuntimeConfigLoader.EnsureTemplateExists(_projectRoot);

        MergeEnvFile(Path.Combine(_projectRoot, ".env"));
        MergeEnvFile(Path.Combine(_projectRoot, "connector", ".env"));
        LocalRuntimeConfigLoader.MergeInto(_config, _projectRoot);
        MergeProcessEnvironment();
        NormalizeDatabaseKeys();
    }

    public bool HasDatabaseConnectionString =>
        !string.IsNullOrWhiteSpace(GetDatabaseConnectionString());

    public string DatabaseConfigMissingMessage =>
        string.IsNullOrWhiteSpace(_projectRoot)
            ? "Database ayarı eksik: appsettings.Local.json"
            : $"Database ayarı eksik: {LocalRuntimeConfigLoader.GetLocalConfigPath(_projectRoot)}";

    public string? GetConnectorSharedSecret() =>
        Get("WHATSAPP_CONNECTOR_SHARED_SECRET") ?? Get("CONNECTOR_SHARED_SECRET");

    public IReadOnlyDictionary<string, string> BuildApiProcessEnvironment(DesktopSettings settings)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in _config)
        {
            env[kv.Key] = kv.Value;
        }

        env["ASPNETCORE_ENVIRONMENT"] = Get("ASPNETCORE_ENVIRONMENT") ?? "Development";
        env["ASPNETCORE_URLS"] = settings.ApiBaseUrl.TrimEnd('/');

        SetDefault(env, "WHATSAPP_CONNECTOR_ENABLED", "true");
        SetDefault(env, "WHATSAPP_CONNECTOR_BASE_URL", $"http://127.0.0.1:{settings.ConnectorPort}");
        SetDefault(env, "WORKFLOW_AUTO_REPLY_ENABLED", "true");

        if (!string.IsNullOrWhiteSpace(settings.ProjectRoot))
        {
            var defaultCableAsset = Path.Combine(settings.ProjectRoot, "assets", "support", "usb-a-usb-b-printer-cable.jpg");
            if (File.Exists(defaultCableAsset))
            {
                SetDefault(env, "WORKFLOW_CONFIG_EXAMPLE_IMAGE_PATH", defaultCableAsset);
            }
        }

        ApplyAspNetCoreKeys(env);

        var secret = GetConnectorSharedSecret();
        if (!string.IsNullOrWhiteSpace(secret))
        {
            env["WHATSAPP_CONNECTOR_SHARED_SECRET"] = secret;
            env["CONNECTOR_SHARED_SECRET"] = secret;
            env["WhatsAppConnector__SharedSecret"] = secret;
        }

        return env;
    }

    public IReadOnlyDictionary<string, string> BuildConnectorProcessEnvironment(DesktopSettings settings)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in _config)
        {
            env[kv.Key] = kv.Value;
        }

        env["PORT"] = settings.ConnectorPort.ToString();
        SetDefault(env, "ASPNET_WEBHOOK_URL", $"{settings.ApiBaseUrl.TrimEnd('/')}/internal/whatsapp/events");

        var secret = GetConnectorSharedSecret();
        if (!string.IsNullOrWhiteSpace(secret))
        {
            env["CONNECTOR_SHARED_SECRET"] = secret;
            env["WHATSAPP_CONNECTOR_SHARED_SECRET"] = secret;
        }

        return env;
    }

    private void MergeEnvFile(string path)
    {
        foreach (var kv in EnvFileLoader.Load(path))
        {
            _config[kv.Key] = kv.Value;
        }
    }

    private void MergeProcessEnvironment()
    {
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is not string key || entry.Value is not string value)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            _config[key] = value;
        }
    }

    private void NormalizeDatabaseKeys()
    {
        var conn = GetDatabaseConnectionString();
        if (string.IsNullOrWhiteSpace(conn))
        {
            return;
        }

        _config["DATABASE_CONNECTION_STRING"] = conn;
        _config["Database__ConnectionString"] = conn;
    }

    private string? GetDatabaseConnectionString() =>
        Get("DATABASE_CONNECTION_STRING") ?? Get("Database__ConnectionString");

    private static void ApplyAspNetCoreKeys(Dictionary<string, string> env)
    {
        var conn = env.TryGetValue("DATABASE_CONNECTION_STRING", out var dbFlat) && !string.IsNullOrWhiteSpace(dbFlat)
            ? dbFlat
            : env.TryGetValue("Database__ConnectionString", out var dbAsp) && !string.IsNullOrWhiteSpace(dbAsp)
                ? dbAsp
                : null;

        if (!string.IsNullOrWhiteSpace(conn))
        {
            env["DATABASE_CONNECTION_STRING"] = conn;
            env["Database__ConnectionString"] = conn;
        }

        CopyAspNet(env, "OPENAI_API_KEY", "OpenAI__ApiKey");
        CopyAspNet(env, "OPENAI_MODEL", "OpenAI__Model");
        CopyAspNet(env, "AI_PROVIDER", "AI__Provider");
        CopyAspNet(env, "GEMINI_API_KEY", "Gemini__ApiKey");
        CopyAspNet(env, "GEMINI_MODEL", "Gemini__Model");
        CopyAspNet(env, "GEMINI_CAPACITY_MODE", "Gemini__CapacityMode");
        CopyAspNet(env, "GEMINI_TIMEOUT_SECONDS", "Gemini__TimeoutSeconds");
        CopyAspNet(env, "GEMINI_MAX_CONCURRENT_REQUESTS", "Gemini__MaxConcurrentRequests");
        CopyAspNet(env, "GEMINI_MAX_RETRIES", "Gemini__MaxRetries");
        CopyAspNet(env, "GEMINI_MAX_RETRY_DELAY_SECONDS", "Gemini__MaxRetryDelaySeconds");
        CopyAspNet(env, "GEMINI_DAILY_QUOTA_AWARE", "Gemini__DailyQuotaAware");
        CopyAspNet(env, "WHATSAPP_CONNECTOR_ENABLED", "WhatsAppConnector__Enabled");
        CopyAspNet(env, "WHATSAPP_CONNECTOR_BASE_URL", "WhatsAppConnector__BaseUrl");
        CopyAspNet(env, "WHATSAPP_CONNECTOR_SHARED_SECRET", "WhatsAppConnector__SharedSecret");
        CopyAspNet(env, "WORKFLOW_AUTO_REPLY_ENABLED", "Workflow__AutoReplyEnabled");
        CopyAspNet(env, "WORKFLOW_CONFIG_EXAMPLE_IMAGE_PATH", "Workflow__ConfigExampleImagePath");
        CopyAspNet(env, "WHATSAPP_TEST_MODE_ENABLED", "WhatsAppTestMode__Enabled");
        CopyAspNet(env, "WHATSAPP_TEST_MODE_ALLOWED_PHONE", "WhatsAppTestMode__AllowedPhoneNumber");
        CopyAspNet(env, "ADMIN_DASHBOARD_PASSWORD", "Admin__DashboardPassword");
        CopyAspNet(env, "ADMIN_SESSION_SECRET", "Admin__SessionSecret");
    }

    private static void CopyAspNet(Dictionary<string, string> env, string flatKey, string aspNetKey)
    {
        if (env.TryGetValue(flatKey, out var flat) && !string.IsNullOrWhiteSpace(flat))
        {
            env[aspNetKey] = flat;
            return;
        }

        if (env.TryGetValue(aspNetKey, out var asp) && !string.IsNullOrWhiteSpace(asp))
        {
            env[flatKey] = asp;
        }
    }

    private static void SetDefault(Dictionary<string, string> env, string key, string value)
    {
        if (!env.ContainsKey(key) || string.IsNullOrWhiteSpace(env[key]))
        {
            env[key] = value;
        }
    }

    private string? Get(string key) =>
        _config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
