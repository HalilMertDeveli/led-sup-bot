using System.IO;
using LedSupBot.Desktop.Models;

namespace LedSupBot.Desktop.Services;

public sealed class RuntimeEnvironmentLoader
{
    private readonly Dictionary<string, string> _rootEnv = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _connectorEnv = new(StringComparer.OrdinalIgnoreCase);

    public RuntimeEnvironmentLoader(DesktopSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ProjectRoot))
        {
            return;
        }

        var rootEnvPath = Path.Combine(settings.ProjectRoot, ".env");
        foreach (var kv in EnvFileLoader.Load(rootEnvPath))
        {
            _rootEnv[kv.Key] = kv.Value;
        }

        var connectorEnvPath = Path.Combine(settings.ProjectRoot, "connector", ".env");
        foreach (var kv in EnvFileLoader.Load(connectorEnvPath))
        {
            _connectorEnv[kv.Key] = kv.Value;
        }

        MergeProcessEnvironment(_rootEnv);
        MergeProcessEnvironment(_connectorEnv);
    }

    public bool HasDatabaseConnectionString =>
        !string.IsNullOrWhiteSpace(Get("DATABASE_CONNECTION_STRING"));

    public string? GetConnectorSharedSecret() =>
        Get("WHATSAPP_CONNECTOR_SHARED_SECRET") ?? Get("CONNECTOR_SHARED_SECRET");

    public IReadOnlyDictionary<string, string> BuildApiProcessEnvironment(DesktopSettings settings)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in _rootEnv)
        {
            env[kv.Key] = kv.Value;
        }

        env["ASPNETCORE_ENVIRONMENT"] = Get("ASPNETCORE_ENVIRONMENT") ?? "Development";
        env["ASPNETCORE_URLS"] = settings.ApiBaseUrl.TrimEnd('/');

        if (!env.ContainsKey("WHATSAPP_CONNECTOR_ENABLED"))
        {
            env["WHATSAPP_CONNECTOR_ENABLED"] = "true";
        }

        if (!env.ContainsKey("WHATSAPP_CONNECTOR_BASE_URL"))
        {
            env["WHATSAPP_CONNECTOR_BASE_URL"] = $"http://127.0.0.1:{settings.ConnectorPort}";
        }

        if (!env.ContainsKey("WORKFLOW_AUTO_REPLY_ENABLED"))
        {
            env["WORKFLOW_AUTO_REPLY_ENABLED"] = "true";
        }

        var secret = GetConnectorSharedSecret();
        if (!string.IsNullOrWhiteSpace(secret))
        {
            env["WHATSAPP_CONNECTOR_SHARED_SECRET"] = secret;
            env["CONNECTOR_SHARED_SECRET"] = secret;
        }

        return env;
    }

    public IReadOnlyDictionary<string, string> BuildConnectorProcessEnvironment(DesktopSettings settings)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in _connectorEnv)
        {
            env[kv.Key] = kv.Value;
        }

        foreach (var kv in _rootEnv)
        {
            env.TryAdd(kv.Key, kv.Value);
        }

        env["PORT"] = settings.ConnectorPort.ToString();

        if (!env.ContainsKey("ASPNET_WEBHOOK_URL"))
        {
            env["ASPNET_WEBHOOK_URL"] = $"{settings.ApiBaseUrl.TrimEnd('/')}/internal/whatsapp/events";
        }

        var secret = GetConnectorSharedSecret();
        if (!string.IsNullOrWhiteSpace(secret))
        {
            env["CONNECTOR_SHARED_SECRET"] = secret;
            env["WHATSAPP_CONNECTOR_SHARED_SECRET"] = secret;
        }

        return env;
    }

    private string? Get(string key)
    {
        if (_rootEnv.TryGetValue(key, out var root))
        {
            return root;
        }

        return _connectorEnv.TryGetValue(key, out var connector) ? connector : null;
    }

    private static void MergeProcessEnvironment(Dictionary<string, string> target)
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

            target.TryAdd(key, value);
        }
    }
}
