using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Binding = System.Windows.Data.Binding;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListBox = System.Windows.Controls.ListBox;
using Orientation = System.Windows.Controls.Orientation;
using SelectionMode = System.Windows.Controls.SelectionMode;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace LedSupBot.Desktop.Views;

/// <summary>
/// Explorer-like FOLDER-ONLY multi selection window. The list uses native WPF extended
/// selection: SHIFT+click selects a continuous range, CTRL+click toggles individual
/// folders, CTRL+A selects all visible folders. Selections are retained while navigating
/// so folders from different locations can be combined before confirming. Cancel keeps
/// every state untouched; only SEÇ returns the explicitly selected folder paths.
/// </summary>
public sealed class PanelMultiFolderSelectionWindow : Window
{
    private readonly ObservableCollection<PanelMultiFolderItem> _items = [];
    private readonly HashSet<string> _selectedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<string> _backStack = [];
    private readonly ListBox _list;
    private readonly System.Windows.Controls.TextBox _pathBox;
    private readonly TextBlock _selectionLabel;
    private bool _loading;
    private string _currentDirectory;

    public IReadOnlyList<string> SelectedPaths => _selectedPaths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

    public PanelMultiFolderSelectionWindow(string initialDirectory)
    {
        _currentDirectory = Path.GetFullPath(initialDirectory);
        Title = "Birden fazla klasör seç";
        Width = 900;
        Height = 640;
        MinWidth = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(22, 27, 34));
        Foreground = Brushes.WhiteSmoke;

        var root = new DockPanel { Margin = new Thickness(16) };
        var heading = new TextBlock
        {
            Text = "Klasörleri CTRL / SHIFT ile seçin (girmek için çift tıklayın)",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var navigation = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(ActionButton("GERİ", GoBack));
        buttons.Children.Add(ActionButton("YUKARI", GoUp));
        buttons.Children.Add(ActionButton("YENİLE", Refresh));
        DockPanel.SetDock(buttons, Dock.Left);
        navigation.Children.Add(buttons);
        _pathBox = new System.Windows.Controls.TextBox
        {
            Margin = new Thickness(8, 0, 8, 8),
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromRgb(34, 40, 49)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(75, 85, 99))
        };
        _pathBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { NavigateFromPath(); e.Handled = true; } };
        navigation.Children.Add(_pathBox);
        var go = ActionButton("YOLA GİT", NavigateFromPath);
        DockPanel.SetDock(go, Dock.Right);
        navigation.Children.Add(go);
        DockPanel.SetDock(navigation, Dock.Top);
        root.Children.Add(navigation);

        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        _selectionLabel = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.LightGray,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        footer.Children.Add(_selectionLabel);
        var footerButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        footerButtons.Children.Add(ActionButton("SEÇİMİ TEMİZLE", ClearSelection));
        footerButtons.Children.Add(ActionButton("İPTAL", Close));
        var confirm = ActionButton("SEÇ", ConfirmSelection);
        confirm.FontWeight = FontWeights.Bold;
        footerButtons.Children.Add(confirm);
        DockPanel.SetDock(footerButtons, Dock.Right);
        footer.Children.Add(footerButtons);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        _list = new ListBox
        {
            ItemsSource = _items,
            SelectionMode = SelectionMode.Extended,
            ItemTemplate = CreateItemTemplate(),
            ItemContainerStyle = CreateItemContainerStyle(),
            Background = new SolidColorBrush(Color.FromRgb(28, 34, 42)),
            Foreground = Brushes.WhiteSmoke,
            BorderBrush = new SolidColorBrush(Color.FromRgb(55, 65, 81))
        };
        _list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        _list.SelectionChanged += ListSelectionChanged;
        _list.MouseDoubleClick += (_, _) => NavigateSelectedDirectory();
        _list.PreviewKeyDown += ListPreviewKeyDown;
        root.Children.Add(_list);

        Content = root;
        LoadDirectory(_currentDirectory, rememberCurrent: false);
    }

    private void ListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        foreach (PanelMultiFolderItem item in e.RemovedItems) _selectedPaths.Remove(item.FullPath);
        foreach (PanelMultiFolderItem item in e.AddedItems) _selectedPaths.Add(item.FullPath);
        UpdateSelectionLabel();
    }

    private void ListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _list.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            NavigateSelectedDirectory();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void NavigateSelectedDirectory()
    {
        if (_list.SelectedItem is PanelMultiFolderItem item)
            LoadDirectory(item.FullPath, rememberCurrent: true);
    }

    private void NavigateFromPath()
    {
        try { LoadDirectory(_pathBox.Text, rememberCurrent: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            System.Windows.MessageBox.Show(ex.Message, "Klasör açılamadı", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void GoBack()
    {
        if (_backStack.Count == 0) return;
        var target = _backStack.Pop();
        LoadDirectory(target, rememberCurrent: false);
    }

    private void GoUp()
    {
        var parent = Directory.GetParent(_currentDirectory);
        if (parent is not null) LoadDirectory(parent.FullName, rememberCurrent: true);
    }

    private void Refresh() => LoadDirectory(_currentDirectory, rememberCurrent: false);

    private void LoadDirectory(string directoryPath, bool rememberCurrent)
    {
        var target = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(target)) throw new DirectoryNotFoundException("Klasör bulunamadı.");

        var nextItems = Directory.EnumerateDirectories(target)
            .OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase)
            .Select(PanelMultiFolderItem.Create)
            .ToList();

        if (rememberCurrent && !target.Equals(_currentDirectory, StringComparison.OrdinalIgnoreCase))
            _backStack.Push(_currentDirectory);
        _currentDirectory = target;
        _pathBox.Text = target;

        _loading = true;
        try
        {
            _items.Clear();
            foreach (var item in nextItems) _items.Add(item);
            _list.SelectedItems.Clear();
            foreach (var item in _items.Where(x => _selectedPaths.Contains(x.FullPath)))
                _list.SelectedItems.Add(item);
        }
        finally { _loading = false; }
        UpdateSelectionLabel();
    }

    private void ClearSelection()
    {
        _selectedPaths.Clear();
        _list.SelectedItems.Clear();
        UpdateSelectionLabel();
    }

    private void UpdateSelectionLabel()
    {
        if (_selectedPaths.Count == 0)
        {
            _selectionLabel.Text = "Seçilen klasör: 0";
            _selectionLabel.ToolTip = null;
            return;
        }
        var names = _selectedPaths
            .Select(path => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
            .ToList();
        _selectionLabel.Text = $"Seçilen klasör: {_selectedPaths.Count} — {string.Join(" ; ", names)}";
        _selectionLabel.ToolTip = string.Join(Environment.NewLine, SelectedPaths);
    }

    private void ConfirmSelection()
    {
        if (_selectedPaths.Count == 0)
        {
            System.Windows.MessageBox.Show("En az bir klasör seçin ya da İPTAL ile kapatın.", "Birden fazla klasör ekle");
            return;
        }
        DialogResult = true;
    }

    private static System.Windows.Controls.Button ActionButton(string text, Action action)
    {
        var button = new System.Windows.Controls.Button
        {
            Content = text,
            Padding = new Thickness(13, 7, 13, 7),
            Margin = new Thickness(0, 0, 8, 8)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static DataTemplate CreateItemTemplate()
    {
        var template = new DataTemplate();
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.FontSizeProperty, 13.0);
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(PanelMultiFolderItem.Name)));
        text.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(PanelMultiFolderItem.FullPath)));
        template.VisualTree = text;
        return template;
    }

    private static Style CreateItemContainerStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(10, 6, 10, 6)));
        return style;
    }

    public record PanelMultiFolderItem(string FullPath, string Name)
    {
        public static PanelMultiFolderItem Create(string path)
        {
            var fullPath = Path.GetFullPath(path);
            return new(fullPath, Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        }
    }
}