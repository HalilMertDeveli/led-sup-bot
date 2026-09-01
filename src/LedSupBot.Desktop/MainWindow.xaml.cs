using System.ComponentModel;
using System.Windows;
using LedSupBot.Desktop.ViewModels;

namespace LedSupBot.Desktop;

public partial class MainWindow : Window
{
    private bool _trayMinimizeEnabled;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => _trayMinimizeEnabled = true;
        StateChanged += OnStateChanged;
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is System.Windows.Controls.PasswordBox box)
        {
            vm.Password = box.Password;
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (!_trayMinimizeEnabled)
        {
            return;
        }

        if (WindowState == WindowState.Minimized && System.Windows.Application.Current is App app && !app.ForceExit)
        {
            app.RequestMinimizeToTray();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_trayMinimizeEnabled)
        {
            base.OnClosing(e);
            return;
        }

        if (System.Windows.Application.Current is App app && !app.ForceExit)
        {
            e.Cancel = true;
            Hide();
            app.RequestMinimizeToTray(showBalloon: false);
            return;
        }

        base.OnClosing(e);
    }
}
