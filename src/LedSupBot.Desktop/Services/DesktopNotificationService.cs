namespace LedSupBot.Desktop.Services;

public sealed class DesktopNotificationService
{
    private TrayIconService? _tray;

    public void AttachTray(TrayIconService tray) => _tray = tray;

    public void ShowAnyDeskAlert(string title, string message) =>
        _tray?.ShowNotification(title, message);
}
