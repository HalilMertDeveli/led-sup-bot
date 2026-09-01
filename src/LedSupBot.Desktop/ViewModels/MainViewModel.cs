using System.Windows.Input;
using LedSupBot.Desktop.Models;
using LedSupBot.Desktop.Services;

namespace LedSupBot.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly LedSupBotApiClient _api;
    private readonly AdminSignalRService _signalR;
    private readonly DesktopNotificationService _notifications;
    private readonly TrayIconService? _tray;
    private readonly SynchronizationContext? _uiContext;

    private bool _isAuthenticated;
    private bool _isBusy;
    private string _password = "";
    private string? _errorMessage;
    private string _whatsAppStatus = "Bağlantı Yok";
    private bool _whatsAppConnected;
    private CompactFocusResponse? _focus;
    private string _botStatusText = "—";
    private string _primaryActionText = "BOTU DURDUR";
    private bool _canPrimaryAction;
    private bool _canTakeOver;
    private bool _showResumeAction;
    private bool _showAnyDeskAlert;

    public MainViewModel(
        LedSupBotApiClient api,
        AdminSignalRService signalR,
        DesktopNotificationService notifications,
        TrayIconService? tray = null)
    {
        _api = api;
        _signalR = signalR;
        _notifications = notifications;
        _tray = tray;
        _uiContext = SynchronizationContext.Current;

        LoginCommand = new AsyncRelayCommand(LoginAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(Password));
        PrimaryActionCommand = new AsyncRelayCommand(PrimaryActionAsync, () => CanPrimaryAction && !IsBusy);
        TakeOverCommand = new AsyncRelayCommand(TakeOverAsync, () => CanTakeOver && !IsBusy);
        DismissAnyDeskCommand = new AsyncRelayCommand(DismissAnyDeskAsync, () => Focus?.ConversationId is not null && !IsBusy);

        _signalR.ConversationUpdated += () => _ = RefreshAsync();
        _signalR.MessageReceived += () => _ = RefreshAsync();
        _signalR.AnyDeskReady += evt =>
        {
            _notifications.ShowAnyDeskAlert("LED Support", evt.IssueSummary ?? "Müşteri AnyDesk aşamasına ulaştı.");
            _ = RefreshAsync();
        };
    }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        private set
        {
            if (SetProperty(ref _isAuthenticated, value))
            {
                RaisePropertyChanged(nameof(ShowLogin));
            }
        }
    }

    public bool ShowLogin => !IsAuthenticated;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string WhatsAppStatus
    {
        get => _whatsAppStatus;
        private set => SetProperty(ref _whatsAppStatus, value);
    }

    public bool WhatsAppConnected
    {
        get => _whatsAppConnected;
        private set => SetProperty(ref _whatsAppConnected, value);
    }

    public CompactFocusResponse? Focus
    {
        get => _focus;
        private set
        {
            if (SetProperty(ref _focus, value))
            {
                UpdateBotControls();
                RaisePropertyChanged(nameof(CustomerName));
                RaisePropertyChanged(nameof(IssueSummary));
                RaisePropertyChanged(nameof(StageLabel));
                RaisePropertyChanged(nameof(ScreenSummary));
                RaisePropertyChanged(nameof(ReceiverSummary));
                RaisePropertyChanged(nameof(PanelStatus));
                RaisePropertyChanged(nameof(ConfigStatus));
                RaisePropertyChanged(nameof(AnyDeskStatus));
                RaisePropertyChanged(nameof(PhotoCountText));
                RaisePropertyChanged(nameof(HasActiveConversation));
                RaisePropertyChanged(nameof(RecentMessages));
            }
        }
    }

    public string CustomerName => Focus?.CustomerDisplayName ?? "Aktif müşteri yok";
    public string IssueSummary => string.IsNullOrWhiteSpace(Focus?.IssueSummary) ? "Henüz sorun özeti oluşturulmadı." : Focus!.IssueSummary!;
    public string StageLabel => Focus?.StageLabel ?? "—";
    public string ScreenSummary => Focus?.ScreenSummary ?? "—";
    public string ReceiverSummary => Focus?.ReceiverSummary ?? "—";
    public string PanelStatus => FormatPanelStatus(Focus);
    public string ConfigStatus => FormatStatus(Focus?.ConfigStatus);
    public string AnyDeskStatus => FormatAnyDeskStatus(Focus);
    public string PhotoCountText => Focus?.PhotoCount > 0 ? $"{Focus.PhotoCount} fotoğraf" : "—";
    public bool HasActiveConversation => Focus?.ConversationId is not null;
    public IReadOnlyList<CompactRecentMessage> RecentMessages => Focus?.RecentMessages ?? [];

    public string BotStatusText
    {
        get => _botStatusText;
        private set => SetProperty(ref _botStatusText, value);
    }

    public string PrimaryActionText
    {
        get => _primaryActionText;
        private set => SetProperty(ref _primaryActionText, value);
    }

    public bool CanPrimaryAction
    {
        get => _canPrimaryAction;
        private set
        {
            if (SetProperty(ref _canPrimaryAction, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool CanTakeOver
    {
        get => _canTakeOver;
        private set
        {
            if (SetProperty(ref _canTakeOver, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool ShowResumeAction
    {
        get => _showResumeAction;
        private set => SetProperty(ref _showResumeAction, value);
    }

    public bool ShowAnyDeskAlert
    {
        get => _showAnyDeskAlert;
        private set => SetProperty(ref _showAnyDeskAlert, value);
    }

    public ICommand LoginCommand { get; }
    public ICommand PrimaryActionCommand { get; }
    public ICommand TakeOverCommand { get; }
    public ICommand DismissAnyDeskCommand { get; }

    public async Task InitializeAsync()
    {
        var status = await _api.GetAuthStatusAsync();
        if (status.Authenticated)
        {
            await OnAuthenticatedAsync();
        }
    }

    private async Task LoginAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _api.LoginAsync(Password);
            await OnAuthenticatedAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OnAuthenticatedAsync()
    {
        IsAuthenticated = true;
        Password = "";
        await _signalR.ConnectAsync();
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (!IsAuthenticated)
        {
            return;
        }

        try
        {
            var system = await _api.GetSystemStatusAsync();
            UpdateWhatsAppStatus(system.WhatsAppConnection);

            var focus = await _api.GetCompactFocusAsync();
            RunOnUi(() =>
            {
                Focus = focus;
                ShowAnyDeskAlert = focus.ShowAnyDeskAlert;
            });
        }
        catch (Exception ex)
        {
            RunOnUi(() => ErrorMessage = ex.Message);
        }
    }

    private void UpdateWhatsAppStatus(string raw)
    {
        var connected = string.Equals(raw, "Connected", StringComparison.OrdinalIgnoreCase);
        RunOnUi(() =>
        {
            WhatsAppConnected = connected;
            WhatsAppStatus = connected ? "Bağlı" : MapWhatsAppStatus(raw);
        });
    }

    private static string MapWhatsAppStatus(string raw) => raw.ToUpperInvariant() switch
    {
        "CONNECTING" => "Bağlanıyor",
        "AUTHENTICATION_REQUIRED" => "QR gerekli",
        "ERROR" => "Hata",
        _ => "Bağlantı Yok",
    };

    private void UpdateBotControls()
    {
        var mode = NormalizeBotMode(Focus?.BotMode);
        var hasConversation = Focus?.ConversationId is not null;

        CanPrimaryAction = hasConversation;
        CanTakeOver = hasConversation && mode != "Human";

        switch (mode)
        {
            case "Bot":
                BotStatusText = "● Bot Aktif";
                PrimaryActionText = "BOTU DURDUR";
                ShowResumeAction = false;
                break;
            case "Paused":
                BotStatusText = "○ Bot Durduruldu";
                PrimaryActionText = "BOTU ÇALIŞTIR";
                ShowResumeAction = true;
                break;
            case "Human":
                BotStatusText = "● Manuel Kontrol";
                PrimaryActionText = "BOTU DEVREYE AL";
                ShowResumeAction = true;
                CanTakeOver = false;
                break;
            default:
                BotStatusText = hasConversation ? "—" : "Konuşma yok";
                PrimaryActionText = "BOTU DURDUR";
                ShowResumeAction = false;
                CanPrimaryAction = false;
                CanTakeOver = false;
                break;
        }

        _tray?.SetBotControls(mode == "Bot", PauseBotAsync, ResumeBotAsync);
    }

    public async Task PauseBotAsync()
    {
        if (Focus?.ConversationId is not { } id || NormalizeBotMode(Focus?.BotMode) != "Bot")
        {
            return;
        }

        await _api.PauseBotAsync(id);
        await RefreshAsync();
    }

    public async Task ResumeBotAsync()
    {
        if (Focus?.ConversationId is not { } id || NormalizeBotMode(Focus?.BotMode) == "Bot")
        {
            return;
        }

        await _api.ResumeBotAsync(id);
        await RefreshAsync();
    }

    private async Task PrimaryActionAsync()
    {
        if (Focus?.ConversationId is not { } id)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var mode = NormalizeBotMode(Focus?.BotMode);
            if (mode == "Bot")
            {
                await _api.PauseBotAsync(id);
            }
            else
            {
                await _api.ResumeBotAsync(id);
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task TakeOverAsync()
    {
        if (Focus?.ConversationId is not { } id)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _api.TakeOverAsync(id);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DismissAnyDeskAsync()
    {
        if (Focus?.ConversationId is not { } id)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _api.DismissAnyDeskAlertAsync(id);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string NormalizeBotMode(string? mode) =>
        mode?.Trim() switch
        {
            "Bot" or "0" => "Bot",
            "Human" or "1" => "Human",
            "Paused" or "2" => "Paused",
            _ => "",
        };

    private static string FormatPanelStatus(CompactFocusResponse? focus)
    {
        if (focus is null)
        {
            return "—";
        }

        return focus.PanelStatus switch
        {
            "alındı" => "✓ Etiket alındı",
            "bekleniyor" => "⏳ Bekleniyor",
            _ => focus.PanelStatus,
        };
    }

    private static string FormatStatus(string? status) => status switch
    {
        "tamam" => "✓ Tamam",
        "bekleniyor" => "⏳ Bekleniyor",
        _ => status ?? "—",
    };

    private static string FormatAnyDeskStatus(CompactFocusResponse? focus)
    {
        if (focus is null)
        {
            return "—";
        }

        return focus.AnyDeskStatus switch
        {
            "alındı" => "✓ Alındı",
            "bekleniyor" => focus.ShowAnyDeskAlert ? "⏳ Handoff hazır" : "⏳ Bekleniyor",
            _ => focus.AnyDeskStatus,
        };
    }

    private void RunOnUi(Action action)
    {
        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            action();
            return;
        }

        _uiContext.Post(_ => action(), null);
    }
}

internal sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _running;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_running && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _running = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await _execute();
        }
        finally
        {
            _running = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
