using System.Drawing;
using System.Windows;
using System.Windows.Forms;

namespace LedSupBot.Desktop.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _botStatusItem;
    private readonly ToolStripMenuItem _pauseItem;
    private MainWindow? _mainWindow;
    private Func<Task>? _pauseBotAsync;
    private Func<Task>? _resumeBotAsync;
    private bool _botActive = true;

    public TrayIconService()
    {
        _botStatusItem = new ToolStripMenuItem("Bot: —") { Enabled = false };
        _pauseItem = new ToolStripMenuItem("Botu Durdur", null, (_, _) => _ = ToggleBotAsync());

        _menu = new ContextMenuStrip();
        _menu.Items.Add("Open LED Support", null, (_, _) => ShowMainWindow());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_botStatusItem);
        _menu.Items.Add(_pauseItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Çıkış", null, (_, _) => RequestExit());

        _icon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Visible = true,
            Text = "LED Support",
            ContextMenuStrip = _menu,
        };

        _icon.DoubleClick += (_, _) => ShowMainWindow();
    }

    public event Action? ExitRequested;

    public void AttachMainWindow(MainWindow window)
    {
        _mainWindow = window;
        _icon.Visible = true;
    }

    public void SetBotControls(bool botActive, Func<Task>? pauseAsync, Func<Task>? resumeAsync)
    {
        _botActive = botActive;
        _pauseBotAsync = pauseAsync;
        _resumeBotAsync = resumeAsync;
        _botStatusItem.Text = botActive ? "Bot: Aktif" : "Bot: Durduruldu";
        _pauseItem.Text = botActive ? "Botu Durdur" : "Botu Çalıştır";
    }

    public void ShowNotification(string title, string message)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.BalloonTipIcon = ToolTipIcon.Info;
        _icon.ShowBalloonTip(5000);
    }

    public void MinimizeToTray(bool showBalloon = true)
    {
        RunOnUi(() =>
        {
            if (_mainWindow is null)
            {
                return;
            }

            _mainWindow.Hide();
            _mainWindow.ShowInTaskbar = false;
        });

        if (showBalloon)
        {
            ShowNotification("LED Support", "Arka planda çalışmaya devam ediyor.");
        }
    }

    public void ShowMainWindow()
    {
        RunOnUi(() =>
        {
            if (_mainWindow is null)
            {
                return;
            }

            if (System.Windows.Application.Current is App app)
            {
                app.ShowMainWindowForeground();
                return;
            }

            _mainWindow.ShowInTaskbar = true;
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            _mainWindow.Focus();
        });
    }

    private void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    private async Task ToggleBotAsync()
    {
        try
        {
            if (_botActive)
            {
                if (_pauseBotAsync is not null)
                {
                    await _pauseBotAsync();
                }
            }
            else if (_resumeBotAsync is not null)
            {
                await _resumeBotAsync();
            }
        }
        catch (Exception ex)
        {
            DesktopLog.Error("TrayToggleBot", ex);
        }
    }

    private void RequestExit()
    {
        _icon.Visible = false;
        ExitRequested?.Invoke();
        RunOnUi(() =>
        {
            if (System.Windows.Application.Current is App app)
            {
                app.RequestForceExit();
            }
            else
            {
                System.Windows.Application.Current.Shutdown();
            }
        });
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                var extracted = Icon.ExtractAssociatedIcon(exePath);
                if (extracted is not null)
                {
                    return extracted;
                }
            }
        }
        catch
        {
            /* fallback below */
        }

        return SystemIcons.Application;
    }
}
