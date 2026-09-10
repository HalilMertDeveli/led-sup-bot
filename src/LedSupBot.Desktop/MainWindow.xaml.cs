using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using LedSupBot.Desktop.ViewModels;

namespace LedSupBot.Desktop;

public partial class MainWindow : Window
{
    private bool _trayMinimizeEnabled;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _trayMinimizeEnabled = true;

        if (DataContext is MainViewModel vm)
        {
            vm.ActiveMessages.CollectionChanged += OnActiveMessagesCollectionChanged;
        }

        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is MainViewModel oldVm)
            {
                oldVm.ActiveMessages.CollectionChanged -= OnActiveMessagesCollectionChanged;
            }
            if (args.NewValue is MainViewModel newVm)
            {
                newVm.ActiveMessages.CollectionChanged += OnActiveMessagesCollectionChanged;
            }
        };
    }

    private void OnActiveMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset)
        {
            Dispatcher.InvokeAsync(() =>
            {
                MessagesScrollViewer?.ScrollToBottom();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void MessageComposerBox_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            if (DataContext is MainViewModel vm && vm.SendMessageCommand.CanExecute(null))
            {
                vm.SendMessageCommand.Execute(null);
            }
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
