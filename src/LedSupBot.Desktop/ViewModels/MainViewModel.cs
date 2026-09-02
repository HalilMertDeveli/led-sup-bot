using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
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

    private bool _isAuthenticated;
    private bool _isBusy;
    private bool _isModeChanging;
    private string _password = "";
    private string? _errorMessage;

    // Health & System
    private string _whatsAppStatus = "Bağlantı Yok";
    private bool _whatsAppConnected;
    private string _dbStatus = "Bağlı";
    private bool _showTestModeBanner;
    private string _testModeLabel = "";

    // Sidebar & Navigation
    private string _searchText = "";
    private string _filterMode = "all";
    private bool _isLoadingConversations;
    private List<ConversationListItemDto> _allConversationsRaw = [];
    private ConversationItemViewModel? _selectedConversation;

    // Active Chat
    private bool _isLoadingMessages;
    private bool _isSendingMessage;
    private string _outboundMessageText = "";
    private CompactFocusResponse? _focus;
    private ConversationDetailDto? _selectedDetail;

    private readonly DesktopSettings? _settings;

    // Technical Panel
    private bool _isTechnicalPanelCollapsed;

    public MainViewModel(
        LedSupBotApiClient api,
        AdminSignalRService signalR,
        DesktopNotificationService notifications,
        DesktopSettings? settings = null,
        TrayIconService? tray = null)
    {
        _api = api;
        _signalR = signalR;
        _notifications = notifications;
        _settings = settings;
        _tray = tray;

        Conversations = new ObservableCollection<ConversationItemViewModel>();
        ActiveMessages = new ObservableCollection<MessageItemViewModel>();
        SupportSteps = new ObservableCollection<SupportStepViewModel>();
        PanelPhotos = new ObservableCollection<PhotoDetailDto>();

        InitializeSupportSteps();

        LoginCommand = new AsyncRelayCommand(LoginAsync, () => !IsBusy && !IsModeChanging && !string.IsNullOrWhiteSpace(Password));
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync, () => CanSendMessage);
        PrimaryActionCommand = new AsyncRelayCommand(PrimaryActionAsync, () => CanPrimaryAction && !IsBusy && !IsModeChanging);
        TakeOverCommand = new AsyncRelayCommand(TakeOverAsync, () => CanTakeOver && !IsBusy && !IsModeChanging);
        DismissAnyDeskCommand = new AsyncRelayCommand(DismissAnyDeskAsync, () => HasActiveConversation && !IsBusy && !IsModeChanging);
        ToggleTechnicalPanelCommand = new RelayCommand(() => IsTechnicalPanelCollapsed = !IsTechnicalPanelCollapsed);
        CopyAnyDeskCommand = new RelayCommand(CopyAnyDeskToClipboard, () => !string.IsNullOrWhiteSpace(AnyDeskAddressText) && AnyDeskAddressText != "—");
        SetFilterCommand = new RelayCommand<string>(mode => FilterMode = mode ?? "all");

        _signalR.ConversationUpdated += OnRealtimeConversationUpdated;
        _signalR.MessageReceived += OnRealtimeMessageReceived;
        _signalR.AnyDeskReady += evt =>
        {
            RunOnUi(() => _notifications.ShowAnyDeskAlert("LED Support", evt.IssueSummary ?? "Müşteri AnyDesk aşamasına ulaştı."));
            _ = RefreshAsync();
        };
    }

    #region Collections

    public ObservableCollection<ConversationItemViewModel> Conversations { get; }
    public ObservableCollection<MessageItemViewModel> ActiveMessages { get; }
    public ObservableCollection<SupportStepViewModel> SupportSteps { get; }
    public ObservableCollection<PhotoDetailDto> PanelPhotos { get; }

    #endregion

    #region Commands

    public ICommand LoginCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand SendMessageCommand { get; }
    public ICommand PrimaryActionCommand { get; }
    public ICommand TakeOverCommand { get; }
    public ICommand DismissAnyDeskCommand { get; }
    public ICommand ToggleTechnicalPanelCommand { get; }
    public ICommand CopyAnyDeskCommand { get; }
    public ICommand SetFilterCommand { get; }

    #endregion

    #region Auth & Global State

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
            RunOnUi(() =>
            {
                if (SetProperty(ref _isBusy, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            });
        }
    }

    public bool IsModeChanging
    {
        get => _isModeChanging;
        private set
        {
            RunOnUi(() =>
            {
                if (SetProperty(ref _isModeChanging, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            });
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
        private set => RunOnUi(() => SetProperty(ref _errorMessage, value));
    }

    public string WhatsAppStatus
    {
        get => _whatsAppStatus;
        private set => RunOnUi(() => SetProperty(ref _whatsAppStatus, value));
    }

    public bool WhatsAppConnected
    {
        get => _whatsAppConnected;
        private set => RunOnUi(() => SetProperty(ref _whatsAppConnected, value));
    }

    public string DbStatus
    {
        get => _dbStatus;
        private set => RunOnUi(() => SetProperty(ref _dbStatus, value));
    }

    public bool ShowTestModeBanner
    {
        get => _showTestModeBanner;
        private set => RunOnUi(() => SetProperty(ref _showTestModeBanner, value));
    }

    public string TestModeLabel
    {
        get => _testModeLabel;
        private set => RunOnUi(() => SetProperty(ref _testModeLabel, value));
    }

    #endregion

    #region Sidebar & Filter Properties

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyConversationFilter();
            }
        }
    }

    public string FilterMode
    {
        get => _filterMode;
        set
        {
            if (SetProperty(ref _filterMode, value))
            {
                RaisePropertyChanged(nameof(IsFilterAll));
                RaisePropertyChanged(nameof(IsFilterBot));
                RaisePropertyChanged(nameof(IsFilterHuman));
                RaisePropertyChanged(nameof(IsFilterPaused));
                ApplyConversationFilter();
            }
        }
    }

    public bool IsFilterAll
    {
        get => FilterMode == "all";
        set
        {
            if (value) FilterMode = "all";
        }
    }

    public bool IsFilterBot
    {
        get => FilterMode == "bot";
        set
        {
            if (value) FilterMode = "bot";
        }
    }

    public bool IsFilterHuman
    {
        get => FilterMode == "human";
        set
        {
            if (value) FilterMode = "human";
        }
    }

    public bool IsFilterPaused
    {
        get => FilterMode == "paused";
        set
        {
            if (value) FilterMode = "paused";
        }
    }

    public bool IsLoadingConversations
    {
        get => _isLoadingConversations;
        private set => RunOnUi(() => SetProperty(ref _isLoadingConversations, value));
    }

    public ConversationItemViewModel? SelectedConversation
    {
        get => _selectedConversation;
        set
        {
            if (SetProperty(ref _selectedConversation, value))
            {
                foreach (var conv in Conversations)
                {
                    conv.IsSelected = conv.Id == _selectedConversation?.Id;
                }

                RaisePropertyChanged(nameof(HasActiveConversation));
                RaisePropertyChanged(nameof(ActiveCustomerTitle));
                RaisePropertyChanged(nameof(ActiveCustomerPhone));
                RaisePropertyChanged(nameof(ActiveStageLabel));
                RaisePropertyChanged(nameof(CanSendMessage));

                UpdateBotControls();

                if (_selectedConversation is not null)
                {
                    _ = LoadSelectedConversationAsync(_selectedConversation.Id);
                }
                else
                {
                    ActiveMessages.Clear();
                    ClearTechnicalDetails();
                }
            }
        }
    }

    public bool HasConversations => Conversations.Count > 0;

    #endregion

    #region Active Chat Properties

    public bool HasActiveConversation => SelectedConversation is not null || HasValidConversationId(Focus?.ConversationId);

    public string ActiveCustomerTitle
    {
        get
        {
            if (SelectedConversation is not null)
            {
                return !string.IsNullOrWhiteSpace(SelectedConversation.DisplayName)
                    ? SelectedConversation.DisplayName
                    : SelectedConversation.CustomerIdentifier;
            }

            return Focus?.CustomerDisplayName ?? "Aktif Konuşma Yok";
        }
    }

    public string ActiveCustomerPhone
    {
        get
        {
            if (_selectedDetail?.Customer is not null)
            {
                return _selectedDetail.Customer.PhoneNumber;
            }
            if (SelectedConversation is not null)
            {
                return SelectedConversation.CustomerIdentifier;
            }
            return "";
        }
    }

    public string ActiveStageLabel
    {
        get
        {
            if (_selectedDetail is not null)
            {
                return _selectedDetail.StageLabel;
            }
            if (SelectedConversation is not null)
            {
                return SelectedConversation.StageLabel;
            }
            return Focus?.StageLabel ?? "—";
        }
    }

    public string BotStatusLabel
    {
        get
        {
            var mode = GetActiveBotMode();
            return mode switch
            {
                "Bot" => "Bot Aktif",
                "Paused" => "Durduruldu",
                "Human" => "Manuel Kontrol",
                _ => HasActiveConversation ? "—" : "Konuşma Yok",
            };
        }
    }

    public string BotModeKind => GetActiveBotMode();

    public string PrimaryActionKind
    {
        get
        {
            var mode = GetActiveBotMode();
            return mode == "Bot" ? "Pause" : "Resume";
        }
    }

    public string PrimaryActionText
    {
        get
        {
            var mode = GetActiveBotMode();
            return mode == "Bot" ? "BOTU DURDUR" : "BOTU DEVREYE AL";
        }
    }

    public bool CanPrimaryAction => HasActiveConversation;
    public bool CanTakeOver => HasActiveConversation && GetActiveBotMode() != "Human";

    public bool ShowAnyDeskAlert
    {
        get
        {
            if (_selectedDetail?.Case is not null)
            {
                return Focus?.ShowAnyDeskAlert == true && Focus?.ConversationId == _selectedDetail.Id;
            }
            return Focus?.ShowAnyDeskAlert == true;
        }
    }

    public string AnyDeskAlertIssueSummary => IssueSummaryText;

    public bool IsLoadingMessages
    {
        get => _isLoadingMessages;
        private set => RunOnUi(() => SetProperty(ref _isLoadingMessages, value));
    }

    public string OutboundMessageText
    {
        get => _outboundMessageText;
        set
        {
            if (SetProperty(ref _outboundMessageText, value))
            {
                RaisePropertyChanged(nameof(CanSendMessage));
            }
        }
    }

    public bool IsSendingMessage
    {
        get => _isSendingMessage;
        private set
        {
            if (SetProperty(ref _isSendingMessage, value))
            {
                RaisePropertyChanged(nameof(CanSendMessage));
            }
        }
    }

    public bool CanSendMessage =>
        HasActiveConversation &&
        !IsSendingMessage &&
        !string.IsNullOrWhiteSpace(OutboundMessageText);

    #endregion

    #region Technical Support Panel Properties

    public bool IsTechnicalPanelCollapsed
    {
        get => _isTechnicalPanelCollapsed;
        set => SetProperty(ref _isTechnicalPanelCollapsed, value);
    }

    public string IssueSummaryText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_selectedDetail?.Case?.IssueSummary))
            {
                return _selectedDetail.Case.IssueSummary;
            }
            if (!string.IsNullOrWhiteSpace(Focus?.IssueSummary))
            {
                return Focus.IssueSummary;
            }
            return "Henüz sorun özeti oluşturulmadı.";
        }
    }

    public string ScreenWidthHeightText
    {
        get
        {
            if (_selectedDetail?.Case?.Screen is { } s)
            {
                return $"{s.WidthCm:0} × {s.HeightCm:0} cm";
            }
            return Focus?.ScreenSummary ?? "—";
        }
    }

    public string PanelStatusText
    {
        get
        {
            var count = PanelPhotos.Count;
            if (count > 0)
            {
                return $"✓ {count} fotoğraf yüklendi";
            }
            return Focus?.PanelStatus == "alındı" ? "✓ Etiket alındı" : "⏳ Bekleniyor";
        }
    }

    public string ReceiverSummaryText
    {
        get
        {
            if (_selectedDetail?.Case?.Receiver is { } r && r.Count > 0)
            {
                return $"{r.Count} Adet Receiver";
            }
            return Focus?.ReceiverSummary ?? "—";
        }
    }

    public string ConfigStatusText
    {
        get
        {
            if (_selectedDetail?.Case?.Config is { } c)
            {
                return c.DisplayText;
            }
            return FormatStatus(Focus?.ConfigStatus);
        }
    }

    public string AnyDeskAddressText
    {
        get
        {
            if (_selectedDetail?.Case?.AnyDesk is { } ad && !string.IsNullOrWhiteSpace(ad.MaskedAddress))
            {
                return ad.MaskedAddress;
            }
            return Focus?.AnyDeskStatus == "alındı" ? "✓ Alındı" : "—";
        }
    }

    public string AnyDeskStatusText
    {
        get
        {
            if (_selectedDetail?.Case?.AnyDesk is not null)
            {
                return "✓ Bilgi Alındı";
            }
            return Focus?.AnyDeskStatus == "alındı" ? "✓ Alındı" : "⏳ Bekleniyor";
        }
    }

    public int PhotoCount => PanelPhotos.Count > 0 ? PanelPhotos.Count : (Focus?.PhotoCount ?? 0);
    public bool HasPanelPhotos => PanelPhotos.Count > 0;

    #endregion

    #region Initialization & Lifecycle

    public async Task InitializeAsync()
    {
        try
        {
            var status = await _api.GetAuthStatusAsync();
            if (status.Authenticated)
            {
                await OnAuthenticatedAsync();
                return;
            }

            var password = LocalRuntimeConfigLoader.GetAdminDashboardPassword(_settings?.ProjectRoot) ?? "led-sup-admin";
            try
            {
                await _api.LoginAsync(password);
                await OnAuthenticatedAsync();
                return;
            }
            catch (Exception ex)
            {
                DesktopLog.Error("AutoAuth", ex);
            }
        }
        catch (Exception ex)
        {
            DesktopLog.Error("Initialize", ex);
            ErrorMessage = MapApiError(ex);
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
            DesktopLog.Error("Login", ex);
            ErrorMessage = MapApiError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OnAuthenticatedAsync()
    {
        RunOnUi(() => IsAuthenticated = true);
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
            // 1. Fetch system status
            var system = await _api.GetSystemStatusAsync();
            var foundation = await _api.GetFoundationStatusAsync();
            var whatsAppConnection = system.WhatsAppConnection;
            if (string.IsNullOrWhiteSpace(whatsAppConnection))
            {
                whatsAppConnection = foundation?.WhatsAppConnection ?? "Disconnected";
            }

            UpdateWhatsAppStatus(whatsAppConnection);
            DbStatus = !string.IsNullOrWhiteSpace(system.Database) ? "Bağlı" : "Kontrol Ediliyor";

            var testModeActive = foundation?.WhatsAppTestMode?.Enabled == true &&
                                 foundation.WhatsAppTestMode.IdentityConfigured == true;
            ShowTestModeBanner = testModeActive;
            TestModeLabel = testModeActive
                ? "TEST MODU — Sadece test telefonu"
                : "";

            // 2. Fetch compact focus
            var focus = await _api.GetCompactFocusAsync();
            Focus = focus;

            // 3. Fetch all conversations list
            await LoadConversationsListAsync();

            // 4. If conversation selected, refresh its details and messages
            if (SelectedConversation is not null)
            {
                await LoadSelectedConversationAsync(SelectedConversation.Id);
            }
            else if (Focus?.ConversationId is { } focusId && focusId != Guid.Empty)
            {
                var matchingItem = Conversations.FirstOrDefault(c => c.Id == focusId);
                if (matchingItem is not null)
                {
                    SelectedConversation = matchingItem;
                }
            }
        }
        catch (Exception ex)
        {
            DesktopLog.Error("Refresh", ex);
            ErrorMessage = MapApiError(ex);
        }
    }

    #endregion

    #region Data Loading & Filtering

    private async Task LoadConversationsListAsync()
    {
        IsLoadingConversations = true;
        try
        {
            var res = await _api.GetConversationsAsync(
                mode: FilterMode,
                search: SearchText);

            _allConversationsRaw = res.Items ?? [];
            ApplyConversationFilter();
        }
        catch (Exception ex)
        {
            DesktopLog.Error("LoadConversationsList", ex);
        }
        finally
        {
            IsLoadingConversations = false;
        }
    }

    private void ApplyConversationFilter()
    {
        RunOnUi(() =>
        {
            var currentSelectedId = SelectedConversation?.Id;
            var query = _allConversationsRaw.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(FilterMode) && FilterMode != "all")
            {
                query = query.Where(c => string.Equals(NormalizeBotMode(c.BotMode), NormalizeBotMode(FilterMode), StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var term = SearchText.Trim().ToLower(new CultureInfo("tr-TR"));
                query = query.Where(c =>
                    (!string.IsNullOrWhiteSpace(c.DisplayName) && c.DisplayName.ToLower(new CultureInfo("tr-TR")).Contains(term)) ||
                    (!string.IsNullOrWhiteSpace(c.CustomerIdentifier) && c.CustomerIdentifier.Contains(term)) ||
                    (!string.IsNullOrWhiteSpace(c.LastMessagePreview) && c.LastMessagePreview.ToLower(new CultureInfo("tr-TR")).Contains(term)));
            }

            var items = query.Select(c => new ConversationItemViewModel
            {
                Id = c.Id,
                CustomerIdentifier = c.CustomerIdentifier,
                DisplayName = c.DisplayName,
                Status = c.Status,
                BotMode = NormalizeBotMode(c.BotMode),
                CurrentStage = c.CurrentStage,
                StageLabel = c.StageLabel,
                LastMessagePreview = c.LastMessagePreview ?? "—",
                UpdatedAt = c.UpdatedAt,
                MessageCount = c.MessageCount,
                RequiresAttention = c.RequiresAttention,
                IsSelected = c.Id == currentSelectedId,
            }).ToList();

            Conversations.Clear();
            foreach (var item in items)
            {
                Conversations.Add(item);
            }

            RaisePropertyChanged(nameof(HasConversations));

            if (currentSelectedId.HasValue)
            {
                _selectedConversation = Conversations.FirstOrDefault(c => c.Id == currentSelectedId.Value);
                if (_selectedConversation is not null)
                {
                    _selectedConversation.IsSelected = true;
                }
            }
        });
    }

    private async Task LoadSelectedConversationAsync(Guid conversationId)
    {
        IsLoadingMessages = true;
        try
        {
            var detailTask = _api.GetConversationDetailAsync(conversationId);
            var messagesTask = _api.GetMessagesAsync(conversationId);

            await Task.WhenAll(detailTask, messagesTask);

            var detail = await detailTask;
            var messagesResponse = await messagesTask;

            _selectedDetail = detail;

            RunOnUi(() =>
            {
                // Update messages
                ActiveMessages.Clear();
                if (messagesResponse?.Messages is { } list)
                {
                    foreach (var msg in list)
                    {
                        var isOutbound = string.Equals(msg.Direction, "Outbound", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(msg.SentBy, "Technician", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(msg.SentBy, "Bot", StringComparison.OrdinalIgnoreCase);

                        ActiveMessages.Add(new MessageItemViewModel
                        {
                            Id = msg.Id,
                            Direction = msg.Direction,
                            SentBy = msg.SentBy,
                            IsOutbound = isOutbound,
                            // CRITICAL REQUIREMENT: Always "Sen" for outbound, "Müşteri" for inbound.
                            DisplaySender = isOutbound ? "Sen" : "Müşteri",
                            Content = msg.Content ?? "",
                            CreatedAt = msg.CreatedAt,
                            DeliveryStatus = msg.DeliveryStatus,
                            MessageType = msg.MessageType,
                            MediaUrl = msg.MediaUrl,
                        });
                    }
                }

                // Update Technical Panel Details
                UpdateTechnicalDetails(detail);
                UpdateSupportSteps(detail);
                UpdateBotControls();

                RaisePropertyChanged(nameof(ActiveCustomerTitle));
                RaisePropertyChanged(nameof(ActiveCustomerPhone));
                RaisePropertyChanged(nameof(ActiveStageLabel));
                RaisePropertyChanged(nameof(IssueSummaryText));
                RaisePropertyChanged(nameof(ScreenWidthHeightText));
                RaisePropertyChanged(nameof(PanelStatusText));
                RaisePropertyChanged(nameof(ReceiverSummaryText));
                RaisePropertyChanged(nameof(ConfigStatusText));
                RaisePropertyChanged(nameof(AnyDeskAddressText));
                RaisePropertyChanged(nameof(AnyDeskStatusText));
                RaisePropertyChanged(nameof(PhotoCount));
                RaisePropertyChanged(nameof(HasPanelPhotos));
                RaisePropertyChanged(nameof(ShowAnyDeskAlert));
            });
        }
        catch (Exception ex)
        {
            DesktopLog.Error("LoadSelectedConversation", ex);
        }
        finally
        {
            IsLoadingMessages = false;
        }
    }

    private void UpdateTechnicalDetails(ConversationDetailDto? detail)
    {
        PanelPhotos.Clear();
        if (detail?.Case?.Photos is { } photos)
        {
            foreach (var p in photos)
            {
                PanelPhotos.Add(p);
            }
        }
    }

    private void ClearTechnicalDetails()
    {
        _selectedDetail = null;
        PanelPhotos.Clear();
        InitializeSupportSteps();
    }

    #endregion

    #region Support Flow Stepper

    private void InitializeSupportSteps()
    {
        SupportSteps.Clear();
        SupportSteps.Add(new SupportStepViewModel { Index = 1, Title = "1. Ekran Boyutu", Status = "Pending", StatusText = "Bekleniyor" });
        SupportSteps.Add(new SupportStepViewModel { Index = 2, Title = "2. Panel Etiketi", Status = "Pending", StatusText = "Bekleniyor" });
        SupportSteps.Add(new SupportStepViewModel { Index = 3, Title = "3. Receiver Kartı", Status = "Pending", StatusText = "Bekleniyor" });
        SupportSteps.Add(new SupportStepViewModel { Index = 4, Title = "4. USB / Config", Status = "Pending", StatusText = "Bekleniyor" });
        SupportSteps.Add(new SupportStepViewModel { Index = 5, Title = "5. AnyDesk", Status = "Pending", StatusText = "Bekleniyor" });
    }

    private void UpdateSupportSteps(ConversationDetailDto? detail)
    {
        if (SupportSteps.Count < 5)
        {
            InitializeSupportSteps();
        }

        var screen = detail?.Case?.Screen;
        var photos = detail?.Case?.Photos;
        var receiver = detail?.Case?.Receiver;
        var config = detail?.Case?.Config;
        var anyDesk = detail?.Case?.AnyDesk;

        // Step 1: Ekran
        if (screen is { WidthCm: > 0, HeightCm: > 0 })
        {
            SupportSteps[0].Status = "Completed";
            SupportSteps[0].StatusText = $"{screen.WidthCm:0}×{screen.HeightCm:0} cm";
        }
        else
        {
            SupportSteps[0].Status = "Active";
            SupportSteps[0].StatusText = "Bekleniyor";
        }

        // Step 2: Panel
        if (photos is { Count: > 0 })
        {
            SupportSteps[1].Status = "Completed";
            SupportSteps[1].StatusText = $"{photos.Count} Fotoğraf";
        }
        else
        {
            SupportSteps[1].Status = SupportSteps[0].IsCompleted ? "Active" : "Pending";
            SupportSteps[1].StatusText = "Bekleniyor";
        }

        // Step 3: Receiver
        if (receiver is { Count: > 0 })
        {
            SupportSteps[2].Status = "Completed";
            SupportSteps[2].StatusText = $"{receiver.Count} Adet";
        }
        else
        {
            SupportSteps[2].Status = SupportSteps[1].IsCompleted ? "Active" : "Pending";
            SupportSteps[2].StatusText = "Bekleniyor";
        }

        // Step 4: Config
        if (config is not null && config.Status == "Confirmed")
        {
            SupportSteps[3].Status = "Completed";
            SupportSteps[3].StatusText = "Bağlandı";
        }
        else
        {
            SupportSteps[3].Status = SupportSteps[2].IsCompleted ? "Active" : "Pending";
            SupportSteps[3].StatusText = config?.DisplayText ?? "Bekleniyor";
        }

        // Step 5: AnyDesk
        if (anyDesk is not null && !string.IsNullOrWhiteSpace(anyDesk.MaskedAddress))
        {
            SupportSteps[4].Status = "Completed";
            SupportSteps[4].StatusText = anyDesk.MaskedAddress;
        }
        else
        {
            SupportSteps[4].Status = SupportSteps[3].IsCompleted ? "Active" : "Pending";
            SupportSteps[4].StatusText = "Bekleniyor";
        }
    }

    #endregion

    #region Outbound Messaging & Operator Actions

    private async Task SendMessageAsync()
    {
        var text = OutboundMessageText.Trim();
        if (string.IsNullOrWhiteSpace(text) || !TryGetActiveConversationId(out var convId))
        {
            return;
        }

        IsSendingMessage = true;
        ErrorMessage = null;
        try
        {
            // Clear input immediately for smooth UX
            OutboundMessageText = "";

            // Optimistically insert into active chat UI
            var localMsg = new MessageItemViewModel
            {
                Id = Guid.NewGuid(),
                Direction = "Outbound",
                SentBy = "Technician",
                IsOutbound = true,
                DisplaySender = "Sen",
                Content = text,
                CreatedAt = DateTimeOffset.Now,
                DeliveryStatus = "Pending",
                MessageType = "Text",
            };
            ActiveMessages.Add(localMsg);

            var sendRes = await _api.SendTextMessageAsync(convId, text);
            localMsg.DeliveryStatus = sendRes.DeliveryStatus ?? "Sent";

            // Trigger refresh to update sidebar previews and stage
            _ = RefreshAsync();
        }
        catch (Exception ex)
        {
            DesktopLog.Error("SendMessage", ex);
            ErrorMessage = MapApiError(ex);
        }
        finally
        {
            IsSendingMessage = false;
        }
    }

    private void CopyAnyDeskToClipboard()
    {
        if (!string.IsNullOrWhiteSpace(AnyDeskAddressText) && AnyDeskAddressText != "—")
        {
            try
            {
                System.Windows.Clipboard.SetText(AnyDeskAddressText);
                ErrorMessage = null;
            }
            catch (Exception ex)
            {
                DesktopLog.Error("CopyAnyDesk", ex);
            }
        }
    }

    private async Task PrimaryActionAsync()
    {
        if (!TryGetActiveConversationId(out var id))
        {
            ErrorMessage = "Aktif konuşma bulunamadı.";
            return;
        }

        await RunProtectedModeChangeAsync("PrimaryAction", async () =>
        {
            var mode = GetActiveBotMode();
            if (mode == "Bot")
            {
                await _api.PauseBotAsync(id);
            }
            else
            {
                await _api.ResumeBotAsync(id);
            }

            await RefreshAsync();
        });
    }

    private async Task TakeOverAsync()
    {
        if (!TryGetActiveConversationId(out var id))
        {
            ErrorMessage = "Aktif konuşma bulunamadı.";
            return;
        }

        await RunProtectedModeChangeAsync("TakeOver", async () =>
        {
            await _api.TakeOverAsync(id);
            await RefreshAsync();
        });
    }

    private async Task DismissAnyDeskAsync()
    {
        if (!TryGetActiveConversationId(out var id))
        {
            return;
        }

        await RunProtectedModeChangeAsync("DismissAnyDesk", async () =>
        {
            await _api.DismissAnyDeskAlertAsync(id);
            await RefreshAsync();
        });
    }

    private async Task RunProtectedModeChangeAsync(string operation, Func<Task> action)
    {
        if (IsModeChanging)
        {
            return;
        }

        IsModeChanging = true;
        ErrorMessage = null;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            var apiEx = ex as LedSupBotApiException;
            DesktopLog.Error(operation, ex, apiEx?.StatusCode);
            ErrorMessage = MapApiError(ex);
        }
        finally
        {
            IsModeChanging = false;
        }
    }

    #endregion

    #region Realtime Events

    private void OnRealtimeConversationUpdated()
    {
        _ = RefreshAsync();
    }

    private void OnRealtimeMessageReceived()
    {
        if (SelectedConversation is not null)
        {
            _ = LoadSelectedConversationAsync(SelectedConversation.Id);
        }
        else
        {
            _ = RefreshAsync();
        }
    }

    #endregion

    #region Helper Methods

    private CompactFocusResponse? Focus
    {
        get => _focus;
        set
        {
            if (SetProperty(ref _focus, value))
            {
                UpdateBotControls();
                RaisePropertyChanged(nameof(HasActiveConversation));
                RaisePropertyChanged(nameof(ActiveCustomerTitle));
                RaisePropertyChanged(nameof(ActiveStageLabel));
                RaisePropertyChanged(nameof(IssueSummaryText));
                RaisePropertyChanged(nameof(ScreenWidthHeightText));
                RaisePropertyChanged(nameof(PanelStatusText));
                RaisePropertyChanged(nameof(ReceiverSummaryText));
                RaisePropertyChanged(nameof(ConfigStatusText));
                RaisePropertyChanged(nameof(AnyDeskAddressText));
                RaisePropertyChanged(nameof(AnyDeskStatusText));
                RaisePropertyChanged(nameof(ShowAnyDeskAlert));
            }
        }
    }

    private bool TryGetActiveConversationId(out Guid id)
    {
        id = Guid.Empty;
        if (SelectedConversation is not null && SelectedConversation.Id != Guid.Empty)
        {
            id = SelectedConversation.Id;
            return true;
        }

        if (Focus?.ConversationId is { } focusId && focusId != Guid.Empty)
        {
            id = focusId;
            return true;
        }

        return false;
    }

    private string GetActiveBotMode()
    {
        if (_selectedDetail is not null)
        {
            return NormalizeBotMode(_selectedDetail.BotMode);
        }
        if (SelectedConversation is not null)
        {
            return NormalizeBotMode(SelectedConversation.BotMode);
        }
        return NormalizeBotMode(Focus?.BotMode);
    }

    private void UpdateBotControls()
    {
        var mode = GetActiveBotMode();
        var hasConversation = HasActiveConversation;

        RaisePropertyChanged(nameof(BotModeKind));
        RaisePropertyChanged(nameof(BotStatusLabel));
        RaisePropertyChanged(nameof(PrimaryActionKind));
        RaisePropertyChanged(nameof(PrimaryActionText));
        RaisePropertyChanged(nameof(CanPrimaryAction));
        RaisePropertyChanged(nameof(CanTakeOver));

        _tray?.SetBotControls(
            mode == "Bot",
            () => RunProtectedModeChangeAsync("TrayPause", () => PrimaryActionAsync()),
            () => RunProtectedModeChangeAsync("TrayResume", () => PrimaryActionAsync()));
    }

    private void UpdateWhatsAppStatus(string raw)
    {
        var connected = string.Equals(raw, "Connected", StringComparison.OrdinalIgnoreCase);
        WhatsAppConnected = connected;
        WhatsAppStatus = connected ? "Bağlı" : MapWhatsAppStatus(raw);
    }

    private static string MapWhatsAppStatus(string raw) => raw.ToUpperInvariant() switch
    {
        "CONNECTING" => "Bağlanıyor",
        "AUTHENTICATION_REQUIRED" => "QR gerekli",
        "ERROR" => "Hata",
        _ => "Bağlantı Yok",
    };

    private static string NormalizeBotMode(string? mode) =>
        mode?.Trim() switch
        {
            "Bot" or "0" => "Bot",
            "Human" or "1" => "Human",
            "Paused" or "2" => "Paused",
            _ => "",
        };

    private static string FormatStatus(string? status) => status switch
    {
        "tamam" => "✓ Tamam",
        "bekleniyor" => "⏳ Bekleniyor",
        _ => status ?? "—",
    };

    private static bool HasValidConversationId(Guid? conversationId) =>
        conversationId is { } id && id != Guid.Empty;

    private static string MapApiError(Exception ex) =>
        ex switch
        {
            LedSupBotApiException apiEx => apiEx.Message,
            _ => "İşlem gerçekleştirilemedi.",
        };

    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    #endregion
}

#region Sub-ViewModels

public sealed class ConversationItemViewModel : ViewModelBase
{
    private bool _isSelected;

    public Guid Id { get; set; }
    public string CustomerIdentifier { get; set; } = "";
    public string? DisplayName { get; set; }
    public string Status { get; set; } = "";
    public string BotMode { get; set; } = "";
    public string CurrentStage { get; set; } = "";
    public string StageLabel { get; set; } = "";
    public string LastMessagePreview { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public int MessageCount { get; set; }
    public bool RequiresAttention { get; set; }

    public string DisplayTitle => !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName : CustomerIdentifier;

    public string FormattedTime
    {
        get
        {
            var local = UpdatedAt.ToLocalTime();
            var now = DateTimeOffset.Now;
            if (local.Date == now.Date)
            {
                return local.ToString("HH:mm");
            }
            if (local.Date == now.Date.AddDays(-1))
            {
                return "Dün";
            }
            return local.ToString("dd.MM");
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class MessageItemViewModel : ViewModelBase
{
    public Guid Id { get; set; }
    public string Direction { get; set; } = "";
    public string SentBy { get; set; } = "";
    public bool IsOutbound { get; set; }
    public string DisplaySender { get; set; } = "Sen";
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string DeliveryStatus { get; set; } = "";
    public string MessageType { get; set; } = "";
    public string? MediaUrl { get; set; }

    public bool HasMedia => !string.IsNullOrWhiteSpace(MediaUrl) || string.Equals(MessageType, "Image", StringComparison.OrdinalIgnoreCase);

    public string FormattedTime => CreatedAt.ToLocalTime().ToString("HH:mm");

    public string FormattedDeliveryStatus => DeliveryStatus.ToLowerInvariant() switch
    {
        "delivered" or "2" => "✓✓",
        "read" or "3" => "✓✓",
        "sent" or "1" => "✓",
        "pending" or "0" => "⏳",
        "failed" or "4" => "⚠",
        _ => "",
    };
}

public sealed class SupportStepViewModel : ViewModelBase
{
    private string _status = "Pending";
    private string _statusText = "Bekleniyor";

    public int Index { get; set; }
    public string Title { get; set; } = "";

    public string Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                RaisePropertyChanged(nameof(IsCompleted));
                RaisePropertyChanged(nameof(IsActive));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsCompleted => string.Equals(Status, "Completed", StringComparison.OrdinalIgnoreCase);
    public bool IsActive => string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase);
}

#endregion

#region Helper Command Implementations

internal sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            _execute();
        }
    }
}

internal sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Predicate<T?>? _canExecute;

    public RelayCommand(Action<T?> execute, Predicate<T?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            _execute((T?)parameter);
        }
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
        catch (Exception ex)
        {
            DesktopLog.Error("Command", ex);
        }
        finally
        {
            _running = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}

#endregion
