using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LedSupBot.Application.PanelFiles;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace LedSupBot.Desktop.Views;

/// <summary>Read-only normalization preview shown before any upload begins.</summary>
public sealed class PanelBulkImportWindow : Window
{
    public ObservableCollection<PanelBulkImportRow> Rows { get; }
    public IReadOnlyList<PanelImportSelectionPackage> SelectedPackages { get; }

    public PanelBulkImportWindow(PanelImportSelectionPlan plan)
    {
        SelectedPackages = plan.ImportablePackages;
        Rows = new ObservableCollection<PanelBulkImportRow>(plan.Packages.Select(x => new PanelBulkImportRow(x)));
        Title = "Panel paketi içe aktarma önizlemesi";
        Width = 1050;
        Height = 620;
        MinWidth = 800;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(22, 27, 34));
        Foreground = Brushes.WhiteSmoke;

        var root = new DockPanel { Margin = new Thickness(16) };
        var heading = new TextBlock
        {
            Text = "Seçim önizlemesi",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 5)
        };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var explanation = new TextBlock
        {
            Text = "Yalnızca READY / REIMPORT satırları içe aktarılır. NO PACKAGE ve INVALID satırları atlanır.",
            Foreground = Brushes.LightGray,
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(explanation, Dock.Top);
        root.Children.Add(explanation);

        var footer = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        footer.Children.Add(ActionButton("İPTAL", Close));
        var import = ActionButton("SEÇİLENLERİ İÇE AKTAR", Confirm);
        import.FontWeight = FontWeights.Bold;
        footer.Children.Add(import);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var grid = new DataGrid
        {
            ItemsSource = Rows,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Extended,
            Background = new SolidColorBrush(Color.FromRgb(28, 34, 42)),
            Foreground = Brushes.WhiteSmoke,
            RowBackground = new SolidColorBrush(Color.FromRgb(28, 34, 42)),
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(32, 39, 48)),
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            BorderBrush = new SolidColorBrush(Color.FromRgb(55, 65, 81))
        };
        grid.Columns.Add(TextColumn("Seçilen öğe", nameof(PanelBulkImportRow.SelectedItem), new DataGridLength(2, DataGridLengthUnitType.Star)));
        grid.Columns.Add(TextColumn("Paket", nameof(PanelBulkImportRow.PackageKey), 210));
        grid.Columns.Add(TextColumn("Dosya", nameof(PanelBulkImportRow.FileCount), 75));
        grid.Columns.Add(TextColumn("Durum", nameof(PanelBulkImportRow.Status), 250));
        root.Children.Add(grid);
        Content = root;
    }

    private void Confirm()
    {
        if (SelectedPackages.Count == 0)
        {
            System.Windows.MessageBox.Show("İçe aktarılabilecek geçerli bir paket yok.", "Panel paketi içe aktarma");
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

public sealed class PanelBulkImportRow
{
    public PanelBulkImportRow(PanelImportSelectionPackage package) => Package = package;
    public PanelImportSelectionPackage Package { get; }
    public string SelectedItem => string.Join("; ", Package.SelectedPaths.Select(Path.GetFileName));
    public string PackageKey => Package.CanonicalKey ?? "—";
    public int FileCount => Package.FilePaths.Count;
    public string Status => Package.Error is null ? Package.Status : $"{Package.Status}: {Package.Error}";
}
