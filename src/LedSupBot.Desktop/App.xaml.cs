using System.ComponentModel;
using System.Windows;
using LedSupBot.Desktop.Models;
using LedSupBot.Desktop.Services;
using LedSupBot.Desktop.ViewModels;
using LedSupBot.Desktop.Views;

namespace LedSupBot.Desktop;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\LedSupBot.LedSupport.Desktop";
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private DesktopSettings? _settings;
    private LocalServiceManager? _serviceManager;
    private LedSupBotApiClient? _apiClient;
    private AdminSignalRService? _signalR;
    private TrayIconService? _tray;
    private DesktopNotificationService? _notifications;
    private MainViewModel? _mainViewModel;
    private MainWindow? _mainWindow;
    private bool _forceExit;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        RegisterGlobalExceptionHandlers();

        // A second Desktop used to overlap the first one's shutdown: it observed
        // Connector/API as healthy, adopted neither process, then the first
        // instance disposed and killed the services it owned. Serialize startup
        // so the replacement either focuses the existing app or waits until its
        // project services have been stopped before performing fresh health checks.
        _singleInstanceMutex = new Mutex(false, SingleInstanceMutexName);
        try
        {
            _ownsSingleInstanceMutex = _singleInstanceMutex.WaitOne(TimeSpan.FromSeconds(3));
        }
        catch (AbandonedMutexException)
        {
            _ownsSingleInstanceMutex = true;
        }

        if (!_ownsSingleInstanceMutex)
        {
            System.Windows.MessageBox.Show(
                "LED Support zaten çalışıyor. Sistem tepsisindeki simgeden mevcut pencereyi açabilirsiniz.",
                "LED Support", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            _settings = DesktopSettingsLoader.Load();
            _serviceManager = new LocalServiceManager(_settings);
            _tray = new TrayIconService();
            _notifications = new DesktopNotificationService();
            _notifications.AttachTray(_tray);

            var startupVm = new StartupViewModel();
            var startupWindow = new StartupWindow { DataContext = startupVm };
            startupWindow.Show();

            await startupVm.RunAsync(_serviceManager);

            await EnsureMainWindowReadyAsync();
            ShowMainWindowForeground();
            startupWindow.Close();
            await _mainViewModel!.InitializeAsync();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Başlatma hatası: {ex.Message}", "LED Support", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    internal void ShowMainWindowForeground()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.ShowInTaskbar = true;
        _mainWindow.Visibility = Visibility.Visible;
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
        _mainWindow.Focus();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
    }

    private Task EnsureMainWindowReadyAsync()
    {
        if (_mainWindow is not null)
        {
            return Task.CompletedTask;
        }

        return Dispatcher.InvokeAsync(() =>
        {
            _apiClient = new LedSupBotApiClient(_settings!.ApiBaseUrl);
            _signalR = new AdminSignalRService(_apiClient, _settings.ApiBaseUrl);
            _mainViewModel = new MainViewModel(_apiClient, _signalR, _notifications!, _settings, _tray);

            _mainWindow = new MainWindow { DataContext = _mainViewModel };
            MainWindow = _mainWindow;
            _tray!.AttachMainWindow(_mainWindow);
            _tray.ExitRequested += OnTrayExitRequested;
        }).Task;
    }

    private static void RegisterGlobalExceptionHandlers()
    {
        Current.DispatcherUnhandledException += (_, args) =>
        {
            DesktopLog.Error("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
            System.Windows.MessageBox.Show(
                "Beklenmeyen bir hata oluştu. Uygulama açık kalacak.",
                "LED Support",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                DesktopLog.Error("UnhandledException", ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            DesktopLog.Error("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    private void OnTrayExitRequested()
    {
        _forceExit = true;
        _serviceManager?.StopStartedServices();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_signalR is not null)
        {
            await _signalR.DisposeAsync();
        }

        _tray?.Dispose();
        _serviceManager?.Dispose();
        _apiClient?.Dispose();
        if (_ownsSingleInstanceMutex)
        {
            try { _singleInstanceMutex?.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    internal void RequestMinimizeToTray(bool showBalloon = true) => _tray?.MinimizeToTray(showBalloon);

    internal bool ForceExit => _forceExit;

    internal void RequestForceExit()
    {
        _forceExit = true;
        _serviceManager?.StopStartedServices();
        Shutdown();
    }
}
