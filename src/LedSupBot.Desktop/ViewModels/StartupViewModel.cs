using LedSupBot.Desktop.Models;
using LedSupBot.Desktop.Services;

namespace LedSupBot.Desktop.ViewModels;

public sealed class StartupViewModel : ViewModelBase
{
    private string _statusText = "Starting...";
    private bool _isReady;
    private bool _hasFailure;

    public string PostgreSqlLine { get; private set; } = "PostgreSQL     … Bekleniyor";
    public string ApiLine { get; private set; } = "API            … Bekleniyor";
    public string ConnectorLine { get; private set; } = "Connector      … Bekleniyor";
    public string WhatsAppLine { get; private set; } = "WhatsApp       … Bekleniyor";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsReady
    {
        get => _isReady;
        private set => SetProperty(ref _isReady, value);
    }

    public bool HasFailure
    {
        get => _hasFailure;
        private set => SetProperty(ref _hasFailure, value);
    }

    public async Task RunAsync(LocalServiceManager serviceManager, CancellationToken cancellationToken = default)
    {
        StatusText = "Starting...";
        await serviceManager.EnsureRuntimeAsync(Update, cancellationToken);

        IsReady = true;
        StatusText = HasFailure ? "READY (eksik servisler var)" : "READY";
    }

    private void Update(RuntimeComponentStatus status)
    {
        switch (status.Name)
        {
            case "PostgreSQL":
                PostgreSqlLine = Pad(status.DisplayLine);
                break;
            case "API":
                ApiLine = Pad(status.DisplayLine);
                break;
            case "Connector":
                ConnectorLine = Pad(status.DisplayLine);
                break;
            case "WhatsApp":
                WhatsAppLine = Pad(status.DisplayLine);
                break;
        }

        if (status.State == ComponentState.Failed)
        {
            HasFailure = true;
        }

        RaisePropertyChanged(nameof(PostgreSqlLine));
        RaisePropertyChanged(nameof(ApiLine));
        RaisePropertyChanged(nameof(ConnectorLine));
        RaisePropertyChanged(nameof(WhatsAppLine));
    }

    private static string Pad(string line)
    {
        if (line.StartsWith("PostgreSQL", StringComparison.Ordinal))
        {
            return "PostgreSQL     " + line["PostgreSQL".Length..].TrimStart();
        }

        if (line.StartsWith("API", StringComparison.Ordinal))
        {
            return "API            " + line["API".Length..].TrimStart();
        }

        if (line.StartsWith("Connector", StringComparison.Ordinal))
        {
            return "Connector      " + line["Connector".Length..].TrimStart();
        }

        if (line.StartsWith("WhatsApp", StringComparison.Ordinal))
        {
            return "WhatsApp       " + line["WhatsApp".Length..].TrimStart();
        }

        return line;
    }
}
