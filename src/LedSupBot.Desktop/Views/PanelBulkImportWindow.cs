using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LedSupBot.Application.PanelFiles;

namespace LedSupBot.Desktop.Views;

public sealed class PanelBulkImportWindow : Window
{
    public ObservableCollection<PanelBulkImportRow> Rows { get; }
    public IReadOnlyList<PanelBulkFolderCandidate> SelectedCandidates => Rows
        .Where(x => x.IsSelected && x.CanSelect).Select(x => x.Candidate).ToList();

    public PanelBulkImportWindow(IReadOnlyList<PanelBulkFolderCandidate> candidates)
    {
        Title = "Toplu panel paketi içe aktarma";
        Width = 980;
        Height = 620;
        MinWidth = 760;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Rows = new ObservableCollection<PanelBulkImportRow>(candidates.Select(x => new PanelBulkImportRow(x)));
        DataContext = this;

        var root = new DockPanel { Margin = new Thickness(16) };
        var heading = new TextBlock
        {
            Text = "İçe aktarılacak paket klasörlerini seçin",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var actions = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        actions.Children.Add(ActionButton("TÜMÜNÜ SEÇ", () => SetSelection(_ => true)));
        actions.Children.Add(ActionButton("SEÇİMİ TEMİZLE", () => SetSelection(_ => false)));
        actions.Children.Add(ActionButton("SADECE GEÇERLİLERİ SEÇ", () => SetSelection(x => x.CanSelect)));
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

        var footer = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var cancel = ActionButton("İPTAL", Close);
        var import = ActionButton("SEÇİLENLERİ İÇE AKTAR", Confirm);
        import.FontWeight = FontWeights.Bold;
        footer.Children.Add(cancel);
        footer.Children.Add(import);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var grid = new DataGrid
        {
            ItemsSource = Rows,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = false,
            SelectionMode = DataGridSelectionMode.Extended,
            Margin = new Thickness(0, 0, 0, 12)
        };
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Seç", Binding = new System.Windows.Data.Binding(nameof(PanelBulkImportRow.IsSelected)) { Mode = BindingMode.TwoWay }, Width = 55 });
        grid.Columns.Add(TextColumn("Klasör", nameof(PanelBulkImportRow.FolderName), 210));
        grid.Columns.Add(TextColumn("Paket Anahtarı", nameof(PanelBulkImportRow.PackageKey), 190));
        grid.Columns.Add(TextColumn("Pitch", nameof(PanelBulkImportRow.Pitch), 75));
        grid.Columns.Add(TextColumn("Chipset", nameof(PanelBulkImportRow.Chipset), 100));
        grid.Columns.Add(TextColumn("Decoder", nameof(PanelBulkImportRow.Decoder), 90));
        grid.Columns.Add(TextColumn("Dosya", nameof(PanelBulkImportRow.FileCount), 65));
        grid.Columns.Add(TextColumn("Durum", nameof(PanelBulkImportRow.Status), 130));
        root.Children.Add(grid);
        Content = root;
    }

    private void SetSelection(Func<PanelBulkImportRow, bool> selector)
    {
        foreach (var row in Rows) row.IsSelected = row.CanSelect && selector(row);
    }

    private void Confirm()
    {
        if (SelectedCandidates.Count == 0)
        {
            System.Windows.MessageBox.Show("İçe aktarmak için en az bir geçerli paket seçin.", "Toplu içe aktarma");
            return;
        }
        DialogResult = true;
    }

    private static System.Windows.Controls.Button ActionButton(string text, Action action)
    {
        var button = new System.Windows.Controls.Button { Content = text, Padding = new Thickness(13, 7, 13, 7), Margin = new Thickness(0, 0, 8, 8) };
        button.Click += (_, _) => action();
        return button;
    }

    private static DataGridTextColumn TextColumn(string header, string property, double width) =>
        new() { Header = header, Binding = new System.Windows.Data.Binding(property), Width = width, IsReadOnly = true };
}

public sealed class PanelBulkImportRow : INotifyPropertyChanged
{
    private bool _isSelected;
    public PanelBulkFolderCandidate Candidate { get; }
    public bool CanSelect => Candidate.Status is "READY" or "REIMPORT";
    public bool IsSelected { get => _isSelected; set { var next = CanSelect && value; if (_isSelected == next) return; _isSelected = next; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public string FolderName => Candidate.FolderName;
    public string PackageKey => Candidate.CanonicalKey ?? "—";
    public string Pitch => Parts.ElementAtOrDefault(0) ?? "—";
    public string Chipset => Parts.ElementAtOrDefault(1) ?? "—";
    public string Decoder => Parts.ElementAtOrDefault(2) ?? "—";
    public int FileCount => Candidate.FileCount;
    public string Status => Candidate.Error is null ? Candidate.Status : $"{Candidate.Status}: {Candidate.Error}";
    private string[] Parts => Candidate.CanonicalKey?.Split('-', 3) ?? [];
    public event PropertyChangedEventHandler? PropertyChanged;
    public PanelBulkImportRow(PanelBulkFolderCandidate candidate) { Candidate = candidate; _isSelected = CanSelect; }
}
