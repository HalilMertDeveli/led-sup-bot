namespace LedSupBot.Desktop.Models;

public sealed class DesktopSettings
{
    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:5080";
    public int ConnectorPort { get; set; } = 3100;
    public string PostgreSqlHost { get; set; } = "127.0.0.1";
    public int PostgreSqlPort { get; set; } = 5432;
    public string PostgreSqlDockerContainer { get; set; } = "led-sup-bot-postgres";
    public bool AutoStartApi { get; set; } = true;
    public bool AutoStartConnector { get; set; } = true;
    public bool AutoStartPostgreSql { get; set; } = true;
    public string ProjectRoot { get; set; } = "";
    public string RuntimeConfigRoot { get; set; } = "";
    public string WhatsAppSessionDirectory { get; set; } = "";
}

public enum ComponentState
{
    Pending,
    Checking,
    Starting,
    Ready,
    Failed,
}

public sealed class RuntimeComponentStatus
{
    public RuntimeComponentStatus(string name, ComponentState state, string detail = "")
    {
        Name = name;
        State = state;
        Detail = detail;
    }

    public string Name { get; }
    public ComponentState State { get; set; }
    public string Detail { get; set; }

    public string DisplayLine => State switch
    {
        ComponentState.Ready => $"{Name}     ● {DetailOrDefault("Hazır")}",
        ComponentState.Failed => $"{Name}     ○ {DetailOrDefault("Bağlantı yok")}",
        ComponentState.Starting => $"{Name}     … {DetailOrDefault("Başlatılıyor")}",
        ComponentState.Checking => $"{Name}     … Kontrol ediliyor",
        _ => $"{Name}     … Bekleniyor",
    };

    private string DetailOrDefault(string fallback) =>
        string.IsNullOrWhiteSpace(Detail) ? fallback : Detail;
}
