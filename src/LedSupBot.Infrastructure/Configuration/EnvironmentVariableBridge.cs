using Microsoft.Extensions.Configuration;

namespace LedSupBot.Infrastructure.Configuration;

/// <summary>
/// Maps flat environment variable names from .env.example into hierarchical options keys.
/// Does not load secrets from disk; relies on process environment / host configuration.
/// </summary>
public static class EnvironmentVariableBridge
{
    private static readonly (string FlatName, string HierarchicalKey)[] Mappings =
    [
        ("DATABASE_CONNECTION_STRING", "Database:ConnectionString"),
        ("OPENAI_API_KEY", "OpenAI:ApiKey"),
        ("OPENAI_MODEL", "OpenAI:Model"),
        ("WHATSAPP_ACCESS_TOKEN", "WhatsApp:AccessToken"),
        ("WHATSAPP_PHONE_NUMBER_ID", "WhatsApp:PhoneNumberId"),
        ("WHATSAPP_BUSINESS_ACCOUNT_ID", "WhatsApp:BusinessAccountId"),
        ("WHATSAPP_VERIFY_TOKEN", "WhatsApp:VerifyToken"),
        ("WHATSAPP_APP_SECRET", "WhatsApp:AppSecret"),
        ("WHATSAPP_GRAPH_API_VERSION", "WhatsApp:GraphApiVersion"),
        ("WHATSAPP_CONNECTOR_BASE_URL", "WhatsAppConnector:BaseUrl"),
        ("WHATSAPP_CONNECTOR_SHARED_SECRET", "WhatsAppConnector:SharedSecret"),
        ("WHATSAPP_CONNECTOR_ENABLED", "WhatsAppConnector:Enabled"),
        ("STORAGE_CONNECTION_STRING", "Storage:ConnectionString"),
        ("STORAGE_CONTAINER_NAME", "Storage:ContainerName"),
        ("WORKFLOW_AUTO_REPLY_ENABLED", "Workflow:AutoReplyEnabled"),
        ("WORKFLOW_CONFIG_EXAMPLE_IMAGE_PATH", "Workflow:ConfigExampleImagePath"),
        ("WORKFLOW_NATURAL_TIMING_ENABLED", "Workflow:NaturalTiming:Enabled"),
        ("WORKFLOW_NATURAL_TIMING_MAX_DELAY_SECONDS", "Workflow:NaturalTiming:MaxDelaySeconds"),
        ("WORKFLOW_INBOUND_COALESCE_ENABLED", "Workflow:InboundCoalescing:Enabled"),
        ("WORKFLOW_INBOUND_COALESCE_WINDOW_SECONDS", "Workflow:InboundCoalescing:WindowSeconds"),
        ("AI_EXTRACTION_CONFIDENCE_THRESHOLD", "AiExtraction:ConfidenceThreshold"),
        ("MEDIA_STORAGE_ROOT", "MediaStorage:RootPath"),
        ("MEDIA_MAX_FILE_SIZE_BYTES", "MediaStorage:MaxFileSizeBytes"),
        ("MEDIA_RETENTION_DAYS", "MediaStorage:RetentionDays"),
        ("ADMIN_DASHBOARD_PASSWORD", "Admin:DashboardPassword"),
        ("ADMIN_SESSION_SECRET", "Admin:SessionSecret")
    ];

    public static IConfigurationBuilder AddFlatEnvironmentVariableBridge(this IConfigurationBuilder builder)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (flatName, hierarchicalKey) in Mappings)
        {
            var value = Environment.GetEnvironmentVariable(flatName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                data[hierarchicalKey] = value;
            }
        }

        // Alias: connector local .env uses CONNECTOR_SHARED_SECRET; prefer WHATSAPP_CONNECTOR_SHARED_SECRET when both set.
        if (!data.ContainsKey("WhatsAppConnector:SharedSecret"))
        {
            var alias = Environment.GetEnvironmentVariable("CONNECTOR_SHARED_SECRET");
            if (!string.IsNullOrWhiteSpace(alias))
            {
                data["WhatsAppConnector:SharedSecret"] = alias;
            }
        }

        if (data.Count > 0)
        {
            builder.AddInMemoryCollection(data);
        }

        return builder;
    }
}
