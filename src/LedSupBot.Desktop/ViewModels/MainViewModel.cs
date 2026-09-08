using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using LedSupBot.Application.PanelFiles;
using LedSupBot.Desktop.Models;
using LedSupBot.Desktop.Services;
using LedSupBot.Desktop.Views;

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
    private bool _realtimeConnected;
    private string _realtimeStatusLabel = "Bağlantı Yok";
    private bool _showTestModeBanner;
    private string _testModeLabel = "";

    // Coalesced Realtime Refresh
    private readonly object _refreshLock = new();
    private CancellationTokenSource? _coalesceCts;
    private bool _isRefreshing;
    private bool _refreshPending;
    private readonly System.Threading.Timer _heartbeatTimer;

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

    // Teacher Mode & Training Data
    private bool _isTrainingDataViewVisible;
    private bool _isLoadingTrainingData;
    private string _trainingStatusFilter = "all";
    private string _trainingIntentFilter = "";
    private string _trainingSourceFilter = "";
    private DateTime? _trainingDateFrom;
    private DateTime? _trainingDateTo;
    private TrainingExampleCountsDto _trainingCounts = new();
    private TrainingExampleDto? _selectedTrainingExample;
    private bool _isPanelFilesViewVisible;
    private bool _isLoadingPanelPackages;
    private string _panelPackageSearch = "", _panelPitchFilter = "", _panelCodeFilter = "", _panelStatusFilter = "all", _panelReadinessFilter = "all";
    private string _newPanelPitch = "P2.5", _newPanelCode = "", _newSecondaryCode = "";
    private PanelPackageDto? _selectedPanelPackage;

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
        TrainingExamples = new ObservableCollection<TrainingExampleDto>();
        PanelPackages = new ObservableCollection<PanelPackageDto>();

        TrainingStatusOptions = ["all", "Approved", "Edited", "Rejected", "Excluded"];
        TrainingIntentOptions = ["", "Acknowledgement", "CasualConversation", "Correction", "GeneralKnowledge", "LedTechnicalQuestion", "LedTroubleshooting", "NewCase", "QuestionAboutCurrentStep", "FlowRecovery", "ResumeWorkflow", "WorkflowInformation", "Unclear"];
        TrainingSourceOptions = ["", "GeneralKnowledge", "BackendState", "DiagnosticState", "Rag", "Vision", "VisionAndRag", "BackendAndRag", "DiagnosticAndRag", "Deterministic", "Mixed"];

        InitializeSupportSteps();

        LoginCommand = new AsyncRelayCommand(LoginAsync, () => !IsBusy && !IsModeChanging && !string.IsNullOrWhiteSpace(Password));
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync, () => CanSendMessage);
        PrimaryActionCommand = new AsyncRelayCommand(PrimaryActionAsync, () => CanPrimaryAction && !IsBusy && !IsModeChanging);
        TakeOverCommand = new AsyncRelayCommand(TakeOverAsync, () => CanTakeOver && !IsBusy && !IsModeChanging);
        DismissAnyDeskCommand = new AsyncRelayCommand(DismissAnyDeskAsync, () => HasActiveConversation && !IsBusy && !IsModeChanging);
        AddCaseMemoryCommand = new AsyncRelayCommand(AddCaseMemoryAsync, () => _selectedDetail?.Case is not null && !IsBusy);
        ToggleTechnicalPanelCommand = new RelayCommand(() => IsTechnicalPanelCollapsed = !IsTechnicalPanelCollapsed);
        CopyAnyDeskCommand = new RelayCommand(CopyAnyDeskToClipboard, () => !string.IsNullOrWhiteSpace(AnyDeskAddressText) && AnyDeskAddressText != "—");
        SetFilterCommand = new RelayCommand<string>(mode => FilterMode = mode ?? "all");
        ApproveTrainingFeedbackCommand = new AsyncRelayCommand<MessageItemViewModel>(
            message => SaveTrainingFeedbackAsync(message, "Approved"),
            CanProvideTrainingFeedback);
        OpenEditedTrainingFeedbackCommand = new RelayCommand<MessageItemViewModel>(
            message => message?.OpenFeedbackEditor("Edited"),
            message => message?.ShowTeacherControls == true && !message.IsSavingFeedback);
        OpenRejectedTrainingFeedbackCommand = new RelayCommand<MessageItemViewModel>(
            message => message?.OpenFeedbackEditor("Rejected"),
            message => message?.ShowTeacherControls == true && !message.IsSavingFeedback);
        SaveTrainingFeedbackCommand = new AsyncRelayCommand<MessageItemViewModel>(
            message => SaveTrainingFeedbackAsync(message, message?.DraftFeedbackStatus ?? ""),
            CanSaveTrainingFeedback);
        CancelTrainingFeedbackCommand = new RelayCommand<MessageItemViewModel>(
            message => message?.CancelFeedbackEditor(),
            message => message?.IsFeedbackEditorOpen == true && !message.IsSavingFeedback);
        ToggleTrainingDataViewCommand = new RelayCommand(ToggleTrainingDataView);
        RefreshTrainingDataCommand = new AsyncRelayCommand(LoadTrainingExamplesAsync, () => !IsLoadingTrainingData);
        TogglePanelFilesViewCommand = new RelayCommand(TogglePanelFilesView);
        RefreshPanelPackagesCommand = new AsyncRelayCommand(LoadPanelPackagesAsync, () => !IsLoadingPanelPackages);
        CreatePanelPackageCommand = new AsyncRelayCommand(CreatePanelPackageAsync, () => !IsLoadingPanelPackages && !string.IsNullOrWhiteSpace(NewPanelPitch) && !string.IsNullOrWhiteSpace(NewPanelCode) && !string.IsNullOrWhiteSpace(NewSecondaryCode));
        ImportPanelFolderCommand = new AsyncRelayCommand(ImportPanelFolderAsync, () => !IsLoadingPanelPackages);
        BulkImportPanelFoldersCommand = new AsyncRelayCommand(BulkImportPanelFoldersAsync, () => !IsLoadingPanelPackages);
        EditPanelPackageCommand = new AsyncRelayCommand(EditPanelPackageAsync, () => SelectedPanelPackage is not null && SelectedPanelPackage.Status != "Archived");
        UploadFirmwareCommand = new AsyncRelayCommand(() => UploadPanelAssetAsync("firmware", ".hex"), () => SelectedPanelPackage is not null && SelectedPanelPackage.Status != "Archived");
        UploadReceiverConfigCommand = new AsyncRelayCommand(() => UploadPanelAssetAsync("receiver-config", ".rcvbp"), () => SelectedPanelPackage is not null && SelectedPanelPackage.Status != "Archived");
        ActivatePanelPackageCommand = new AsyncRelayCommand(ActivatePanelPackageAsync, () => SelectedPanelPackage?.Status == "Draft");
        ArchivePanelPackageCommand = new AsyncRelayCommand(ArchivePanelPackageAsync, () => SelectedPanelPackage is not null && SelectedPanelPackage.Status != "Archived");
        DeletePanelPackageCommand = new AsyncRelayCommand(DeletePanelPackageAsync, () => SelectedPanelPackage is not null && SelectedPanelPackage.Status != "Archived");
        RollbackPanelPackageCommand = new AsyncRelayCommand(RollbackPanelPackageAsync, () => SelectedPanelPackage?.Version > 1);

        _signalR.ConnectionStateChanged += (state, label) =>
        {
            RunOnUi(() =>
            {
                RealtimeConnected = state == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected;
                RealtimeStatusLabel = label ?? (RealtimeConnected ? "Canlı" : "Bağlantı Yok");
            });
        };

        _signalR.Reconnected += () =>
        {
            DesktopLog.Info("SignalR", "reconnected -> triggering full recovery refresh");
            ScheduleCoalescedRefresh(TimeSpan.Zero);
        };

        _signalR.StateChanged += () =>
        {
            ScheduleCoalescedRefresh(TimeSpan.FromMilliseconds(250));
        };

        _signalR.ConversationUpdated += _ =>
        {
            ScheduleCoalescedRefresh(TimeSpan.FromMilliseconds(250));
        };

        _signalR.MessageReceived += (_, _) =>
        {
            ScheduleCoalescedRefresh(TimeSpan.FromMilliseconds(250));
        };

        _signalR.PanelPackagesChanged += (_, _) =>
        {
            if (IsPanelFilesViewVisible) RunOnUi(() => _ = LoadPanelPackagesAsync());
        };

        _signalR.WhatsAppStatusChanged += status =>
        {
            if (!string.IsNullOrWhiteSpace(status))
            {
                RunOnUi(() => UpdateWhatsAppStatus(status));
            }
            ScheduleCoalescedRefresh(TimeSpan.FromMilliseconds(250));
        };

        _signalR.AnyDeskReady += evt =>
        {
            RunOnUi(() => _notifications.ShowAnyDeskAlert("LED Support", evt.IssueSummary ?? "Müşteri AnyDesk aşamasına ulaştı."));
            ScheduleCoalescedRefresh(TimeSpan.Zero);
        };

        _heartbeatTimer = new System.Threading.Timer(_ =>
        {
            if (IsAuthenticated && !IsBusy)
            {
                ScheduleCoalescedRefresh(TimeSpan.Zero);
            }
        }, null, TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(45));
    }

    #region Collections

    public ObservableCollection<ConversationItemViewModel> Conversations { get; }
    public ObservableCollection<MessageItemViewModel> ActiveMessages { get; }
    public ObservableCollection<SupportStepViewModel> SupportSteps { get; }
    public ObservableCollection<PhotoDetailDto> PanelPhotos { get; }
    public ObservableCollection<TrainingExampleDto> TrainingExamples { get; }

    public IReadOnlyList<string> TrainingStatusOptions { get; }
    public IReadOnlyList<string> TrainingIntentOptions { get; }
    public IReadOnlyList<string> TrainingSourceOptions { get; }

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
    public ICommand ApproveTrainingFeedbackCommand { get; }
    public ICommand OpenEditedTrainingFeedbackCommand { get; }
    public ICommand OpenRejectedTrainingFeedbackCommand { get; }
    public ICommand SaveTrainingFeedbackCommand { get; }
    public ICommand CancelTrainingFeedbackCommand { get; }
    public ICommand ToggleTrainingDataViewCommand { get; }
    public ICommand RefreshTrainingDataCommand { get; }
    public ICommand TogglePanelFilesViewCommand { get; }
    public ICommand RefreshPanelPackagesCommand { get; }
    public ICommand CreatePanelPackageCommand { get; }
    public ICommand ImportPanelFolderCommand { get; }
    public ICommand BulkImportPanelFoldersCommand { get; }
    public ICommand EditPanelPackageCommand { get; }
    public ICommand UploadFirmwareCommand { get; }
    public ICommand UploadReceiverConfigCommand { get; }
    public ICommand ActivatePanelPackageCommand { get; }
    public ICommand ArchivePanelPackageCommand { get; }
    public ICommand DeletePanelPackageCommand { get; }
    public ICommand RollbackPanelPackageCommand { get; }
    public ICommand AddCaseMemoryCommand { get; }

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

    public bool RealtimeConnected
    {
        get => _realtimeConnected;
        private set => RunOnUi(() => SetProperty(ref _realtimeConnected, value));
    }

    public string RealtimeStatusLabel
    {
        get => _realtimeStatusLabel;
        private set => RunOnUi(() => SetProperty(ref _realtimeStatusLabel, value));
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

    #region Teacher Mode & Training Data Properties

    public ObservableCollection<PanelPackageDto> PanelPackages { get; }
    public IReadOnlyList<string> PanelStatusOptions { get; } = ["all", "Draft", "Active", "Archived"];
    public IReadOnlyList<string> PanelReadinessOptions { get; } = ["all", "READY", "INCOMPLETE", "NOT_DELIVERY_ELIGIBLE"];
    public bool IsPanelFilesViewVisible { get => _isPanelFilesViewVisible; private set { if (SetProperty(ref _isPanelFilesViewVisible, value)) { RaisePropertyChanged(nameof(PanelFilesToggleText)); RaisePropertyChanged(nameof(IsConversationWorkspaceVisible)); } } }
    public bool IsConversationWorkspaceVisible => !IsPanelFilesViewVisible && !IsTrainingDataViewVisible;
    public string PanelFilesToggleText => IsPanelFilesViewVisible ? "💬 Konuşmalara Dön" : "📦 Panel Dosyaları";
    public bool IsLoadingPanelPackages { get => _isLoadingPanelPackages; private set { if (SetProperty(ref _isLoadingPanelPackages, value)) CommandManager.InvalidateRequerySuggested(); } }
    public string PanelPackageSearch { get => _panelPackageSearch; set => SetProperty(ref _panelPackageSearch, value ?? ""); }
    public string PanelPitchFilter { get => _panelPitchFilter; set => SetProperty(ref _panelPitchFilter, value ?? ""); }
    public string PanelCodeFilter { get => _panelCodeFilter; set => SetProperty(ref _panelCodeFilter, value ?? ""); }
    public string PanelStatusFilter { get => _panelStatusFilter; set => SetProperty(ref _panelStatusFilter, value ?? "all"); }
    public string PanelReadinessFilter { get => _panelReadinessFilter; set => SetProperty(ref _panelReadinessFilter, value ?? "all"); }
    public string NewPanelPitch { get => _newPanelPitch; set { if (SetProperty(ref _newPanelPitch, value ?? "")) RaisePropertyChanged(nameof(NewPanelCanonicalPreview)); } }
    public string NewPanelCode { get => _newPanelCode; set { if (SetProperty(ref _newPanelCode, value ?? "")) RaisePropertyChanged(nameof(NewPanelCanonicalPreview)); } }
    public string NewSecondaryCode { get => _newSecondaryCode; set { if (SetProperty(ref _newSecondaryCode, value ?? "")) RaisePropertyChanged(nameof(NewPanelCanonicalPreview)); } }
    public string NewPanelCanonicalPreview => $"{NewPanelPitch.Trim().ToUpperInvariant()}-{NewPanelCode.Trim().ToUpperInvariant()}-{NewSecondaryCode.Trim().ToUpperInvariant()}";
    public PanelPackageDto? SelectedPanelPackage { get => _selectedPanelPackage; set { if (SetProperty(ref _selectedPanelPackage, value)) CommandManager.InvalidateRequerySuggested(); } }

    public bool IsTrainingDataViewVisible
    {
        get => _isTrainingDataViewVisible;
        private set
        {
            if (SetProperty(ref _isTrainingDataViewVisible, value))
            {
                RaisePropertyChanged(nameof(TrainingDataToggleText));
                RaisePropertyChanged(nameof(IsConversationWorkspaceVisible));
            }
        }
    }

    public string TrainingDataToggleText => IsTrainingDataViewVisible
        ? "💬 Konuşmalara Dön"
        : "🧠 Eğitim Verileri";

    public bool IsLoadingTrainingData
    {
        get => _isLoadingTrainingData;
        private set
        {
            if (SetProperty(ref _isLoadingTrainingData, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string TrainingStatusFilter
    {
        get => _trainingStatusFilter;
        set => SetProperty(ref _trainingStatusFilter, value ?? "all");
    }

    public string TrainingIntentFilter
    {
        get => _trainingIntentFilter;
        set => SetProperty(ref _trainingIntentFilter, value ?? "");
    }

    public string TrainingSourceFilter
    {
        get => _trainingSourceFilter;
        set => SetProperty(ref _trainingSourceFilter, value ?? "");
    }

    public DateTime? TrainingDateFrom
    {
        get => _trainingDateFrom;
        set => SetProperty(ref _trainingDateFrom, value);
    }

    public DateTime? TrainingDateTo
    {
        get => _trainingDateTo;
        set => SetProperty(ref _trainingDateTo, value);
    }

    public TrainingExampleCountsDto TrainingCounts
    {
        get => _trainingCounts;
        private set => SetProperty(ref _trainingCounts, value);
    }

    public TrainingExampleDto? SelectedTrainingExample
    {
        get => _selectedTrainingExample;
        set => SetProperty(ref _selectedTrainingExample, value);
    }

    public bool HasTrainingExamples => TrainingExamples.Count > 0;

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

    public string ReceiverModelText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_selectedDetail?.Case?.Receiver?.Model))
            {
                return _selectedDetail.Case.Receiver.Model;
            }
            if (!string.IsNullOrWhiteSpace(Focus?.ReceiverModel))
            {
                return Focus.ReceiverModel;
            }
            return "—";
        }
    }

    public string ReceiverCountText
    {
        get
        {
            if (_selectedDetail?.Case?.Receiver is { Count: > 0 } r)
            {
                return $"{r.Count} Adet";
            }
            if (Focus?.ReceiverCount is > 0)
            {
                return $"{Focus.ReceiverCount} Adet";
            }
            return "—";
        }
    }

    public string ReceiverSummaryText
    {
        get
        {
            if (_selectedDetail?.Case?.Receiver is { } r)
            {
                if (r.Count > 0 && !string.IsNullOrWhiteSpace(r.Model))
                {
                    return $"{r.Count} Adet {r.Model}";
                }
                if (r.Count > 0)
                {
                    return $"{r.Count} Adet";
                }
                if (!string.IsNullOrWhiteSpace(r.Model))
                {
                    return r.Model;
                }
            }
            return Focus?.ReceiverSummary ?? "—";
        }
    }

    public string PlayerTypeText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_selectedDetail?.Case?.PlayerType))
            {
                return _selectedDetail.Case.PlayerType;
            }
            if (!string.IsNullOrWhiteSpace(Focus?.PlayerType))
            {
                return Focus.PlayerType;
            }
            return "—";
        }
    }

    public bool IsProcessorPlayer =>
        string.Equals(PlayerTypeText, "Processor", StringComparison.OrdinalIgnoreCase);

    public string ProcessorHdmiStatusText
    {
        get
        {
            if (_selectedDetail?.Case?.PlayerType is { } pt &&
                string.Equals(pt, "Processor", StringComparison.OrdinalIgnoreCase))
            {
                return _selectedDetail.Case.ProcessorHdmiConnected == true ? "✓ Bağlı" : "⏳ Bekleniyor";
            }
            if (!string.IsNullOrWhiteSpace(Focus?.ProcessorHdmiStatus))
            {
                return Focus.ProcessorHdmiStatus == "Bağlı" ? "✓ Bağlı" : "⏳ Bekleniyor";
            }
            return "—";
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

    public string AiOperatorSummaryText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_selectedDetail?.Case?.AiOperatorSummary))
            {
                return _selectedDetail.Case.AiOperatorSummary;
            }
            if (!string.IsNullOrWhiteSpace(Focus?.AiOperatorSummary))
            {
                return Focus.AiOperatorSummary;
            }
            return "—";
        }
    }

    public int CompletedCasesCount => _selectedDetail?.Case?.CompletedCasesCount ?? Focus?.CompletedCasesCount ?? 0;
    public bool HasCompletedCases => CompletedCasesCount > 0;
    public string CompletedCasesBadgeText => CompletedCasesCount > 0 ? $"{CompletedCasesCount} Tamamlanan Kayıt" : "";

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

    public Task RefreshAsync()
    {
        ScheduleCoalescedRefresh(TimeSpan.Zero);
        return Task.CompletedTask;
    }

    private async Task RefreshInternalAsync()
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

            if (IsTrainingDataViewVisible)
            {
                await LoadTrainingExamplesAsync();
            }
        }
        catch (Exception ex)
        {
            DesktopLog.Error("RefreshInternal", ex);
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
                            TeacherFeedbackEligible = msg.TeacherFeedbackEligible,
                            TrainingFeedback = msg.TrainingFeedback,
                            PanelRecognition = msg.PanelRecognition,
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
                RaisePropertyChanged(nameof(AiOperatorSummaryText));
                RaisePropertyChanged(nameof(CompletedCasesCount));
                RaisePropertyChanged(nameof(HasCompletedCases));
                RaisePropertyChanged(nameof(CompletedCasesBadgeText));
                RaisePropertyChanged(nameof(ScreenWidthHeightText));
                RaisePropertyChanged(nameof(PanelStatusText));
                RaisePropertyChanged(nameof(ReceiverModelText));
                RaisePropertyChanged(nameof(ReceiverCountText));
                RaisePropertyChanged(nameof(ReceiverSummaryText));
                RaisePropertyChanged(nameof(PlayerTypeText));
                RaisePropertyChanged(nameof(IsProcessorPlayer));
                RaisePropertyChanged(nameof(ProcessorHdmiStatusText));
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

    private async Task AddCaseMemoryAsync()
    {
        if (_selectedDetail?.Case is not { } supportCase) return;
        try
        {
            var draft = await _api.CreateMemoryDraftAsync(supportCase.Id);
            if (draft is null) return;
            var dialog = new CaseMemoryReviewWindow(draft) { Owner = System.Windows.Application.Current.MainWindow };
            var approved = dialog.ShowDialog();
            if (dialog.Rejected)
            {
                await _api.RejectMemoryAsync(draft.Id);
                System.Windows.MessageBox.Show("Vaka hafızası taslağı reddedildi.", "Vaka Hafızası");
                return;
            }
            if (approved != true) return;
            await _api.UpdateMemoryDraftAsync(draft.Id, dialog.Edit);
            await _api.VerifyMemoryAsync(draft.Id);
            System.Windows.MessageBox.Show("Çözüm doğrulanmış vaka hafızasına eklendi.", "Vaka Hafızası");
        }
        catch (Exception ex)
        {
            DesktopLog.Error("AddCaseMemory", ex);
            System.Windows.MessageBox.Show(ex.Message, "Vaka Hafızası", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    #region Teacher Mode & Training Data

    private static bool CanProvideTrainingFeedback(MessageItemViewModel? message) =>
        message is { ShowTeacherControls: true, IsSavingFeedback: false };

    private static bool CanSaveTrainingFeedback(MessageItemViewModel? message)
    {
        if (message is not { ShowTeacherControls: true, IsSavingFeedback: false, IsFeedbackEditorOpen: true })
        {
            return false;
        }

        return !string.Equals(message.DraftFeedbackStatus, "Edited", StringComparison.OrdinalIgnoreCase) ||
               (message.HasPanelRecognition
                   ? !string.IsNullOrWhiteSpace(message.DraftPixelPitch) && !string.IsNullOrWhiteSpace(message.DraftPanelCode) && !string.IsNullOrWhiteSpace(message.DraftSecondaryCode)
                   : !string.IsNullOrWhiteSpace(message.DraftPreferredTechnicianResponse));
    }

    private async Task SaveTrainingFeedbackAsync(MessageItemViewModel? message, string feedbackStatus)
    {
        if (message is null || !message.ShowTeacherControls || message.Id == Guid.Empty)
        {
            return;
        }

        if (feedbackStatus is not ("Approved" or "Edited" or "Rejected"))
        {
            message.FeedbackValidationMessage = "Geçerli bir geri bildirim türü seçin.";
            return;
        }

        var preferredResponse = string.Equals(feedbackStatus, "Approved", StringComparison.Ordinal) || message.HasPanelRecognition
            ? null
            : NullIfWhiteSpace(message.DraftPreferredTechnicianResponse);
        if (string.Equals(feedbackStatus, "Edited", StringComparison.Ordinal) && preferredResponse is null && !message.HasPanelRecognition)
        {
            message.FeedbackValidationMessage = "Düzeltilmiş ideal yanıt boş bırakılamaz.";
            return;
        }

        var reasonTags = string.Equals(feedbackStatus, "Approved", StringComparison.Ordinal)
            ? []
            : message.FeedbackReasonOptions.Where(x => x.IsSelected).Select(x => x.Value).ToList();

        message.IsSavingFeedback = true;
        message.FeedbackValidationMessage = null;
        ErrorMessage = null;
        try
        {
            await _api.SaveTrainingFeedbackAsync(
                message.Id,
                new SaveTrainingFeedbackRequest
                {
                    FeedbackStatus = feedbackStatus,
                    PreferredTechnicianResponse = preferredResponse,
                    ReasonTags = reasonTags,
                    PreferredPixelPitch = message.HasPanelRecognition && feedbackStatus == "Edited" ? message.DraftPixelPitch : null,
                    PreferredPanelCode = message.HasPanelRecognition && feedbackStatus == "Edited" ? message.DraftPanelCode : null,
                    PreferredSecondaryCode = message.HasPanelRecognition && feedbackStatus == "Edited" ? message.DraftSecondaryCode : null,
                });

            message.ApplySavedFeedback(feedbackStatus, preferredResponse, reasonTags);

            if (IsTrainingDataViewVisible)
            {
                await LoadTrainingExamplesAsync();
            }

            // The existing SignalR path will also refresh other operator windows.
            // This local refresh keeps the clicked row in sync even if the hub is reconnecting.
            ScheduleCoalescedRefresh(TimeSpan.Zero);
        }
        catch (Exception ex)
        {
            DesktopLog.Error("SaveTrainingFeedback", ex);
            message.FeedbackValidationMessage = MapApiError(ex);
            ErrorMessage = MapApiError(ex);
        }
        finally
        {
            message.IsSavingFeedback = false;
        }
    }

    private void ToggleTrainingDataView()
    {
        IsTrainingDataViewVisible = !IsTrainingDataViewVisible;
        if (IsTrainingDataViewVisible) IsPanelFilesViewVisible = false;
        if (IsTrainingDataViewVisible)
        {
            _ = LoadTrainingExamplesAsync();
        }
    }

    private void TogglePanelFilesView()
    {
        IsPanelFilesViewVisible = !IsPanelFilesViewVisible;
        if (IsPanelFilesViewVisible) { IsTrainingDataViewVisible = false; _ = LoadPanelPackagesAsync(); }
    }

    private async Task LoadPanelPackagesAsync()
    {
        IsLoadingPanelPackages = true; ErrorMessage = null;
        try
        {
            var result = await _api.GetPanelPackagesAsync(PanelPackageSearch, PanelPitchFilter, PanelCodeFilter, PanelStatusFilter, PanelReadinessFilter);
            RunOnUi(() => { PanelPackages.Clear(); foreach (var item in result.Items) PanelPackages.Add(item); SelectedPanelPackage = PanelPackages.FirstOrDefault(); });
        }
        catch (Exception ex) { DesktopLog.Error("LoadPanelPackages", ex); ErrorMessage = MapApiError(ex); }
        finally { IsLoadingPanelPackages = false; }
    }

    private async Task CreatePanelPackageAsync()
    {
        try
        {
            var created = await _api.CreatePanelPackageAsync(new PanelPackageWriteRequest { PixelPitch = NewPanelPitch, PanelCode = NewPanelCode, SecondaryCode = NewSecondaryCode });
            await LoadPanelPackagesAsync(); if (created is not null) SelectedPanelPackage = PanelPackages.FirstOrDefault(x => x.Id == created.Id);
        }
        catch (Exception ex) { ErrorMessage = MapApiError(ex); }
    }

    private async Task ImportPanelFolderAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Panel dosya klasörünü seçin" };
        if (dialog.ShowDialog() != true) return;
        IsLoadingPanelPackages = true; ErrorMessage = null;
        try
        {
            var imported = await _api.ImportPanelFolderAsync(dialog.FolderName);
            await LoadPanelPackagesAsync();
            if (imported is not null) SelectedPanelPackage = PanelPackages.FirstOrDefault(x => x.Id == imported.Id);
        }
        catch (Exception ex) { DesktopLog.Error("ImportPanelFolder", ex); ErrorMessage = MapApiError(ex); }
        finally { IsLoadingPanelPackages = false; }
    }

    private async Task BulkImportPanelFoldersAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Panel paketlerinin bulunduğu üst klasörü seçin" };
        if (dialog.ShowDialog() != true) return;
        IReadOnlyList<PanelBulkFolderCandidate> candidates;
        try
        {
            var existing = PanelPackages.Select(x => x.CanonicalKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            candidates = PanelBulkFolderDiscovery.Discover(dialog.FolderName, existing);
        }
        catch (Exception ex) { ErrorMessage = MapApiError(ex); return; }

        var previewWindow = new PanelBulkImportWindow(candidates) { Owner = System.Windows.Application.Current.MainWindow };
        if (previewWindow.ShowDialog() != true) return;
        var selected = previewWindow.SelectedCandidates;

        IsLoadingPanelPackages = true; var imported = 0; var failed = 0;
        var results = new List<string>();
        try
        {
            foreach (var item in selected)
            {
                try { await _api.ImportPanelFolderAsync(item.FolderPath); imported++; results.Add($"{item.FolderName}: BAŞARILI"); }
                catch (Exception ex) { failed++; results.Add($"{item.FolderName}: BAŞARISIZ — {MapApiError(ex)}"); DesktopLog.Error("BulkImportPanelFolder", ex); }
            }
            await LoadPanelPackagesAsync();
            var details = string.Join(Environment.NewLine, results.Take(80));
            System.Windows.MessageBox.Show($"Seçilen: {selected.Count}\nBaşarılı: {imported}\nBaşarısız: {failed}\nGeçersiz: {candidates.Count(x => x.Status == "INVALID")}\n\n{details}",
                "Toplu içe aktarma sonucu", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        finally { IsLoadingPanelPackages = false; }
    }

    private async Task UploadPanelAssetAsync(string slot, string extension)
    {
        if (SelectedPanelPackage is null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = $"{extension} dosyası|*{extension}", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        try { SelectedPanelPackage = await _api.UploadPanelAssetAsync(SelectedPanelPackage.Id, slot, dialog.FileName); await LoadPanelPackagesAsync(); }
        catch (Exception ex) { ErrorMessage = MapApiError(ex); }
    }

    private async Task EditPanelPackageAsync()
    {
        if (SelectedPanelPackage is null) return;
        var selectedId = SelectedPanelPackage.Id;
        var dialog = new PanelPackageEditWindow(SelectedPanelPackage) { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;
        try
        {
            await _api.UpdatePanelPackageAsync(selectedId, dialog.Edit);
            await LoadPanelPackagesAsync();
            SelectedPanelPackage = PanelPackages.FirstOrDefault(x => x.Id == selectedId);
        }
        catch (Exception ex) { ErrorMessage = MapApiError(ex); }
    }

    private async Task ActivatePanelPackageAsync()
    {
        if (SelectedPanelPackage is null) return;
        try { await _api.ActivatePanelPackageAsync(SelectedPanelPackage.Id); await LoadPanelPackagesAsync(); }
        catch (Exception ex) { ErrorMessage = MapApiError(ex); }
    }

    private async Task ArchivePanelPackageAsync()
    {
        if (SelectedPanelPackage is null) return;
        try { await _api.ArchivePanelPackageAsync(SelectedPanelPackage.Id); await LoadPanelPackagesAsync(); }
        catch (Exception ex) { ErrorMessage = MapApiError(ex); }
    }

    private async Task DeletePanelPackageAsync()
    {
        if (SelectedPanelPackage is null) return;
        var selected = SelectedPanelPackage;
        var confirmation = System.Windows.MessageBox.Show(
            $"{selected.CanonicalKey} paketini silmek istediğinize emin misiniz?\n\nPaket yeni gönderimler için kapatılacak; geçmiş teslimat kayıtları korunacaktır.",
            "Panel paketini sil", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
        if (confirmation != System.Windows.MessageBoxResult.Yes) return;
        try
        {
            await _api.DeletePanelPackageAsync(selected.Id);
            await LoadPanelPackagesAsync();
        }
        catch (Exception ex) { ErrorMessage = MapApiError(ex); }
    }

    private async Task RollbackPanelPackageAsync()
    {
        if (SelectedPanelPackage is null) return;
        var selected = SelectedPanelPackage;
        if (selected.Version <= 1) { ErrorMessage = "Önceki sürüm bulunmuyor."; return; }
        var confirmation = System.Windows.MessageBox.Show(
            $"{selected.CanonicalKey} paketini bir önceki geçerli sürüme geri almak istiyor musunuz?",
            "Paket sürümünü geri al", System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
        if (confirmation != System.Windows.MessageBoxResult.Yes) return;
        try { await _api.RollbackPanelPackageAsync(selected.Id); await LoadPanelPackagesAsync(); }
        catch (Exception ex) { ErrorMessage = MapApiError(ex); }
    }

    private async Task LoadTrainingExamplesAsync()
    {
        if (!IsAuthenticated || IsLoadingTrainingData)
        {
            return;
        }

        IsLoadingTrainingData = true;
        try
        {
            var response = await _api.GetTrainingExamplesAsync(
                TrainingStatusFilter,
                TrainingIntentFilter,
                TrainingSourceFilter,
                TrainingDateFrom,
                TrainingDateTo);

            RunOnUi(() =>
            {
                var selectedId = SelectedTrainingExample?.Id;
                TrainingCounts = response.Counts ?? new TrainingExampleCountsDto();
                TrainingExamples.Clear();
                foreach (var example in response.Items ?? [])
                {
                    TrainingExamples.Add(example);
                }

                SelectedTrainingExample = selectedId is { } id
                    ? TrainingExamples.FirstOrDefault(x => x.Id == id) ?? TrainingExamples.FirstOrDefault()
                    : TrainingExamples.FirstOrDefault();
                RaisePropertyChanged(nameof(HasTrainingExamples));
            });
        }
        catch (Exception ex)
        {
            DesktopLog.Error("LoadTrainingExamples", ex);
            ErrorMessage = MapApiError(ex);
        }
        finally
        {
            IsLoadingTrainingData = false;
        }
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    #endregion

    #region Realtime Events & Coalescing

    public void ScheduleCoalescedRefresh(TimeSpan delay)
    {
        if (!IsAuthenticated)
        {
            return;
        }

        lock (_refreshLock)
        {
            if (delay <= TimeSpan.Zero)
            {
                _coalesceCts?.Cancel();
                _coalesceCts?.Dispose();
                _coalesceCts = null;
                _ = TriggerRefreshLoopAsync();
                return;
            }

            _coalesceCts?.Cancel();
            _coalesceCts?.Dispose();
            _coalesceCts = new CancellationTokenSource();
            var token = _coalesceCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, token);
                    if (!token.IsCancellationRequested)
                    {
                        await TriggerRefreshLoopAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    // Debounced by a newer event - expected
                }
            }, token);
        }
    }

    private async Task TriggerRefreshLoopAsync()
    {
        lock (_refreshLock)
        {
            if (_isRefreshing)
            {
                _refreshPending = true;
                return;
            }
            _isRefreshing = true;
        }

        try
        {
            do
            {
                lock (_refreshLock)
                {
                    _refreshPending = false;
                }

                await RefreshInternalAsync();

            } while (_refreshPending);
        }
        finally
        {
            lock (_refreshLock)
            {
                _isRefreshing = false;
            }
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
                RaisePropertyChanged(nameof(AiOperatorSummaryText));
                RaisePropertyChanged(nameof(CompletedCasesCount));
                RaisePropertyChanged(nameof(HasCompletedCases));
                RaisePropertyChanged(nameof(CompletedCasesBadgeText));
                RaisePropertyChanged(nameof(ScreenWidthHeightText));
                RaisePropertyChanged(nameof(PanelStatusText));
                RaisePropertyChanged(nameof(ReceiverModelText));
                RaisePropertyChanged(nameof(ReceiverCountText));
                RaisePropertyChanged(nameof(ReceiverSummaryText));
                RaisePropertyChanged(nameof(PlayerTypeText));
                RaisePropertyChanged(nameof(IsProcessorPlayer));
                RaisePropertyChanged(nameof(ProcessorHdmiStatusText));
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
    private static readonly string[] FeedbackReasonValues =
    [
        "Tone",
        "TooLong",
        "TooShort",
        "RepeatedQuestion",
        "WrongWorkflow",
        "WrongContext",
        "WrongTechnicalInformation",
        "WrongSource",
        "Hallucination",
        "ShouldUseRag",
        "ShouldNotUseRag",
        "WrongDiagnosticStep",
        "DidNotUnderstandCustomer",
        "TooRobotic",
        "Other",
    ];

    private bool _teacherFeedbackEligible;
    private TrainingFeedbackSummaryDto? _trainingFeedback;
    private bool _isFeedbackEditorOpen;
    private bool _isSavingFeedback;
    private string _draftFeedbackStatus = "";
    private string _draftPreferredTechnicianResponse = "";
    private string? _feedbackValidationMessage;
    private string _draftPixelPitch = "";
    private string _draftPanelCode = "";
    private string _draftSecondaryCode = "";

    public MessageItemViewModel()
    {
        FeedbackReasonOptions = new ObservableCollection<FeedbackReasonOptionViewModel>(
            FeedbackReasonValues.Select(value => new FeedbackReasonOptionViewModel(value)));
    }

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
    public PanelRecognitionDto? PanelRecognition { get; set; }
    public bool HasPanelRecognition => PanelRecognition is not null;
    public string PanelRecognitionSource => PanelRecognition?.SourceSummary ?? "";
    public string DraftPixelPitch { get => _draftPixelPitch; set { if (SetProperty(ref _draftPixelPitch, value)) CommandManager.InvalidateRequerySuggested(); } }
    public string DraftPanelCode { get => _draftPanelCode; set { if (SetProperty(ref _draftPanelCode, value)) CommandManager.InvalidateRequerySuggested(); } }
    public string DraftSecondaryCode { get => _draftSecondaryCode; set { if (SetProperty(ref _draftSecondaryCode, value)) CommandManager.InvalidateRequerySuggested(); } }

    public bool TeacherFeedbackEligible
    {
        get => _teacherFeedbackEligible;
        set
        {
            if (SetProperty(ref _teacherFeedbackEligible, value))
            {
                RaisePropertyChanged(nameof(ShowTeacherControls));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool ShowTeacherControls => TeacherFeedbackEligible;

    public TrainingFeedbackSummaryDto? TrainingFeedback
    {
        get => _trainingFeedback;
        set
        {
            if (SetProperty(ref _trainingFeedback, value))
            {
                SynchronizeFeedbackDisplay();
            }
        }
    }

    public bool HasTrainingFeedback => TrainingFeedback is not null &&
                                       !string.IsNullOrWhiteSpace(TrainingFeedback.FeedbackStatus);

    public string TrainingFeedbackStatusLabel => TrainingFeedback?.StatusLabel
        ?? TrainingFeedback?.FeedbackStatus switch
        {
            "Approved" => "✓ Eğitim verisine eklendi",
            "Edited" => "✎ Düzeltilmiş",
            "Rejected" => "✕ Eğitim için reddedildi",
            _ => "",
        };

    public string TrainingEligibilityLabel => string.IsNullOrWhiteSpace(TrainingFeedback?.TrainingEligibility)
        ? ""
        : TrainingFeedback.TrainingEligibility;

    public ObservableCollection<FeedbackReasonOptionViewModel> FeedbackReasonOptions { get; }

    public bool IsFeedbackEditorOpen
    {
        get => _isFeedbackEditorOpen;
        private set
        {
            if (SetProperty(ref _isFeedbackEditorOpen, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsSavingFeedback
    {
        get => _isSavingFeedback;
        set
        {
            if (SetProperty(ref _isSavingFeedback, value))
            {
                RaisePropertyChanged(nameof(FeedbackSaveButtonText));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string DraftFeedbackStatus
    {
        get => _draftFeedbackStatus;
        private set
        {
            if (SetProperty(ref _draftFeedbackStatus, value))
            {
                RaisePropertyChanged(nameof(FeedbackEditorTitle));
                RaisePropertyChanged(nameof(FeedbackEditorHint));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string DraftPreferredTechnicianResponse
    {
        get => _draftPreferredTechnicianResponse;
        set
        {
            if (SetProperty(ref _draftPreferredTechnicianResponse, value))
            {
                FeedbackValidationMessage = null;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string FeedbackEditorTitle => DraftFeedbackStatus == "Rejected"
        ? "Yanlış olarak işaretle"
        : "İdeal yanıtı düzelt";

    public string FeedbackEditorHint => DraftFeedbackStatus == "Rejected"
        ? "Doğru yanıt isteğe bağlıdır; eklerseniz gelecekte tercih verisi olabilir."
        : "AI yanıtı başlangıç olarak eklendi. Tercih ettiğiniz yanıtı düzenleyin.";

    public string FeedbackSaveButtonText => IsSavingFeedback ? "KAYDEDİLİYOR…" : "KAYDET";

    public string? FeedbackValidationMessage
    {
        get => _feedbackValidationMessage;
        set => SetProperty(ref _feedbackValidationMessage, value);
    }

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

    public void OpenFeedbackEditor(string status)
    {
        if (!ShowTeacherControls || status is not ("Edited" or "Rejected"))
        {
            return;
        }

        DraftFeedbackStatus = status;
        DraftPreferredTechnicianResponse = status == "Edited"
            ? TrainingFeedback?.PreferredTechnicianResponse ?? Content
            : TrainingFeedback?.PreferredTechnicianResponse ?? "";
        DraftPixelPitch = TrainingFeedback?.PreferredPanelIdentity?.PixelPitch ?? PanelRecognition?.PixelPitch ?? "";
        DraftPanelCode = TrainingFeedback?.PreferredPanelIdentity?.PanelCode ?? PanelRecognition?.PanelCode ?? "";
        DraftSecondaryCode = TrainingFeedback?.PreferredPanelIdentity?.SecondaryCode ?? PanelRecognition?.SecondaryCode ?? "";
        var selectedTags = TrainingFeedback?.ReasonTags ?? [];
        foreach (var option in FeedbackReasonOptions)
        {
            option.IsSelected = selectedTags.Contains(option.Value, StringComparer.OrdinalIgnoreCase);
        }

        FeedbackValidationMessage = null;
        IsFeedbackEditorOpen = true;
    }

    public void CancelFeedbackEditor()
    {
        IsFeedbackEditorOpen = false;
        FeedbackValidationMessage = null;
    }

    public void ApplySavedFeedback(string status, string? preferredResponse, IReadOnlyList<string> reasonTags)
    {
        TrainingFeedback = new TrainingFeedbackSummaryDto
        {
            FeedbackStatus = status,
            PreferredTechnicianResponse = preferredResponse,
            ReasonTags = reasonTags.ToList(),
            // Eligibility remains backend-owned; the scheduled refresh replaces these
            // temporary display values with the persisted authoritative result.
            TrainingEligibility = "",
            EligibleForSft = false,
            EligibleForPreference = false,
        };
        IsFeedbackEditorOpen = false;
        FeedbackValidationMessage = null;
    }

    private void SynchronizeFeedbackDisplay()
    {
        RaisePropertyChanged(nameof(HasTrainingFeedback));
        RaisePropertyChanged(nameof(TrainingFeedbackStatusLabel));
        RaisePropertyChanged(nameof(TrainingEligibilityLabel));
    }
}

public sealed class FeedbackReasonOptionViewModel : ViewModelBase
{
    private bool _isSelected;

    public FeedbackReasonOptionViewModel(string value) => Value = value;

    public string Value { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
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

internal sealed class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Predicate<T?>? _canExecute;
    private bool _running;

    public AsyncRelayCommand(Func<T?, Task> execute, Predicate<T?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) =>
        !_running && (_canExecute?.Invoke((T?)parameter) ?? true);

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
            await _execute((T?)parameter);
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
