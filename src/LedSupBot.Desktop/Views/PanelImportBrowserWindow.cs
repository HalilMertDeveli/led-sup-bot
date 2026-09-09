using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LedSupBot.Application.PanelFiles;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace LedSupBot.Desktop.Views;

/// <summary>
/// Application-controlled file browser whose DataGrid uses native WPF extended
/// selection. Selections are retained while navigating so folders and files from
/// adjacent levels can be combined in one import operation.
/// </summary>
public sealed class PanelImportBrowserWindow : Window
{
    private readonly ObservableCollection<PanelImportBrowserItem> _items = [];
    private readonly HashSet<string> _selectedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<string> _backStack = [];
    private readonly DataGrid _grid;
    private readonly System.Windows.Controls.TextBox _pathBox;
    private readonly TextBlock _selectionLabel;
    private bool _loading;
    private string _currentDirectory;

    public IReadOnlyList<string> SelectedPaths => _selectedPaths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

    public PanelImportBrowserWindow(string initialDirectory)
    {
        _currentDirectory = Path.GetFullPath(initialDirectory);
        Title = "Panel paketi öğelerini seç";
        Width = 1050;
        Height = 680;
        MinWidth = 800;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(22, 27, 34));
        Foreground = Brushes.WhiteSmoke;

        var root = new DockPanel { Margin = new Thickness(16) };
        var heading = new TextBlock
        {
            Text = "Dosya ve klasörleri CTRL / SHIFT ile seçin",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var navigation = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
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
        _selectionLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.LightGray };
        footer.Children.Add(_selectionLabel);
        var footerButtons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        footerButtons.Children.Add(ActionButton("SEÇİMİ TEMİZLE", ClearSelection));
        footerButtons.Children.Add(ActionButton("İPTAL", Close));
        var preview = ActionButton("ÖNİZLE", ConfirmSelection);
        preview.FontWeight = FontWeights.Bold;
        footerButtons.Children.Add(preview);
        DockPanel.SetDock(footerButtons, Dock.Right);
        footer.Children.Add(footerButtons);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        _grid = new DataGrid
        {
            ItemsSource = _items,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            Background = new SolidColorBrush(Color.FromRgb(28, 34, 42)),
            Foreground = Brushes.WhiteSmoke,
            RowBackground = new SolidColorBrush(Color.FromRgb(28, 34, 42)),
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(32, 39, 48)),
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            BorderBrush = new SolidColorBrush(Color.FromRgb(55, 65, 81))
        };
        _grid.Columns.Add(TextColumn("Ad", nameof(PanelImportBrowserItem.Name), new DataGridLength(2, DataGridLengthUnitType.Star)));
        _grid.Columns.Add(TextColumn("Tür", nameof(PanelImportBrowserItem.Type), 90));
        _grid.Columns.Add(TextColumn("Boyut / Dosya", nameof(PanelImportBrowserItem.SizeOrCount), 125));
        _grid.Columns.Add(TextColumn("Paket", nameof(PanelImportBrowserItem.Package), 220));
        _grid.Columns.Add(TextColumn("Durum", nameof(PanelImportBrowserItem.Status), 150));
        _grid.SelectionChanged += GridSelectionChanged;
        _grid.MouseDoubleClick += (_, _) => NavigateSelectedDirectory();
        _grid.PreviewKeyDown += GridPreviewKeyDown;
        root.Children.Add(_grid);

        Content = root;
        LoadDirectory(_currentDirectory, rememberCurrent: false);
    }

    private void GridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        foreach (PanelImportBrowserItem item in e.RemovedItems) _selectedPaths.Remove(item.FullPath);
        foreach (PanelImportBrowserItem item in e.AddedItems) _selectedPaths.Add(item.FullPath);
        UpdateSelectionLabel();
    }

    private void GridPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _grid.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            NavigateSelectedDirectory();
            e.Handled = true;
        }
    }

    private void NavigateSelectedDirectory()
    {
        if (_grid.SelectedItem is PanelImportBrowserItem { IsDirectory: true } item)
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
        var nextItems = Directory.EnumerateFileSystemEntries(target, "*", SearchOption.TopDirectoryOnly)
            .Select(PanelImportBrowserItem.Create)
            .OrderByDescending(x => x.IsDirectory)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
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
            _grid.SelectedItems.Clear();
            foreach (var item in _items.Where(x => _selectedPaths.Contains(x.FullPath)))
                _grid.SelectedItems.Add(item);
        }
        finally { _loading = false; }
        UpdateSelectionLabel();
    }

    private void ClearSelection()
    {
        _selectedPaths.Clear();
        _grid.SelectedItems.Clear();
        UpdateSelectionLabel();
    }

    private void UpdateSelectionLabel() => _selectionLabel.Text = $"Seçilen öğe: {_selectedPaths.Count}";

    private void ConfirmSelection()
    {
        if (_selectedPaths.Count == 0)
        {
            System.Windows.MessageBox.Show("Önizlemek için en az bir dosya veya klasör seçin.", "Panel paketi içe aktarma");
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

    private static DataGridTextColumn TextColumn(string header, string property, DataGridLength width) =>
        new() { Header = header, Binding = new System.Windows.Data.Binding(property), Width = width };

    private static DataGridTextColumn TextColumn(string header, string property, double width) =>
        TextColumn(header, property, new DataGridLength(width));
}

public sealed record PanelImportBrowserItem(string FullPath, string Name, bool IsDirectory, string Type,
    string SizeOrCount, string Package, string Status)
{
    public static PanelImportBrowserItem Create(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var attributes = File.GetAttributes(fullPath);
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        var isReparse = (attributes & FileAttributes.ReparsePoint) != 0;
        var identityPath = isDirectory ? fullPath : Path.GetDirectoryName(fullPath)!;
        var package = PanelImportSelectionNormalizer.TryGetCanonicalKey(identityPath, out var key) ? key : "—";
        var status = isReparse ? "BAĞLANTI DESTEKLENMİYOR" : package == "—" ? "PACKAGE IDENTITY REQUIRED" : "SEÇİLEBİLİR";
        var sizeOrCount = isDirectory ? SafeFileCount(fullPath) : new FileInfo(fullPath).Length.ToString("N0") + " bayt";
        return new(fullPath, Path.GetFileName(fullPath), isDirectory, isDirectory ? "Klasör" : "Dosya",
            sizeOrCount, package, status);
    }

    private static string SafeFileCount(string directoryPath)
    {
        try { return Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly).Count() + " dosya"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "Erişilemiyor"; }
    }
}
