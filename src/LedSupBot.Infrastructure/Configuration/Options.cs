namespace LedSupBot.Infrastructure.Configuration;

/// <summary>
/// Bound from configuration / environment. Values must come from env or secret store — never commit secrets.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Connection string via env DATABASE_CONNECTION_STRING preferred in production.</summary>
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4.1-mini";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    public string AccessToken { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
    public string BusinessAccountId { get; set; } = string.Empty;
    public string VerifyToken { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string GraphApiVersion { get; set; } = "v21.0";
}

/// <summary>Unofficial Baileys Node connector settings (Phase 4.5).</summary>
public sealed class WhatsAppConnectorOptions
{
    public const string SectionName = "WhatsAppConnector";

    public string BaseUrl { get; set; } = "http://127.0.0.1:3100";

    public string SharedSecret { get; set; } = string.Empty;

    public bool Enabled { get; set; }
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = "led-sup-bot-media";
}

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = ["http://localhost:5173"];
}
