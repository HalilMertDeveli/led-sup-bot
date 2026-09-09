using System.Windows;
using System.Windows.Controls;
using LedSupBot.Desktop.Models;
using WpfPanel = System.Windows.Controls.Panel;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LedSupBot.Desktop.Views;

public sealed class CaseMemoryReviewWindow : Window
{
    private readonly WpfTextBox _symptom = Box(); private readonly WpfTextBox _model = Box();
    private readonly WpfTextBox _category = Box(); private readonly WpfTextBox _diagnostic = Box(true);
    private readonly WpfTextBox _resolution = Box(true); private readonly WpfTextBox _summary = Box(true);
    public bool Rejected { get; private set; }
    public CaseMemoryEditRequest Edit => new()
    {
        PrimarySymptom = _symptom.Text, DeviceModel = _model.Text, ProblemCategory = _category.Text,
        DiagnosticSummary = _diagnostic.Text, FinalResolution = _resolution.Text,
        ResolutionSummary = _summary.Text, Confidence = .7
    };

    public CaseMemoryReviewWindow(CaseMemoryDto memory)
    {
        Title = "Çözümü Hafızaya Ekle"; Width = 560; Height = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _symptom.Text = memory.PrimarySymptom; _model.Text = memory.DeviceModel ?? ""; _category.Text = memory.ProblemCategory ?? "";
        _diagnostic.Text = memory.DiagnosticSummary; _resolution.Text = memory.FinalResolution; _summary.Text = memory.ResolutionSummary;
        var panel = new StackPanel { Margin = new Thickness(18) };
        Add(panel, "Problem / belirti", _symptom); Add(panel, "Cihaz / model", _model); Add(panel, "Kategori", _category);
        Add(panel, "Yapılan kontroller", _diagnostic); Add(panel, "Nihai çözüm", _resolution); Add(panel, "Kısa faydalı not", _summary);
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var approve = new System.Windows.Controls.Button { Content = "Onayla", Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(6) };
        approve.Click += (_, _) => { if (string.IsNullOrWhiteSpace(_resolution.Text)) { System.Windows.MessageBox.Show("Onay için nihai çözümü yazın."); return; } DialogResult = true; };
        var reject = new System.Windows.Controls.Button { Content = "Reddet", Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(6) };
        reject.Click += (_, _) => { Rejected = true; DialogResult = false; };
        var cancel = new System.Windows.Controls.Button { Content = "Vazgeç", Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(6) };
        cancel.Click += (_, _) => Close(); buttons.Children.Add(cancel); buttons.Children.Add(reject); buttons.Children.Add(approve);
        panel.Children.Add(buttons); Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    private static WpfTextBox Box(bool multiline = false) => new() { MinHeight = multiline ? 70 : 30, TextWrapping = TextWrapping.Wrap, AcceptsReturn = multiline, Margin = new Thickness(0, 3, 0, 10) };
    private static void Add(WpfPanel panel, string label, UIElement input) { panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold }); panel.Children.Add(input); }
}
