using System.Windows;
using System.Windows.Controls;
using LedSupBot.Desktop.Models;
using WpfPanel = System.Windows.Controls.Panel;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LedSupBot.Desktop.Views;

public sealed class PanelPackageEditWindow : Window
{
    private readonly WpfTextBox _pitch = Box();
    private readonly WpfTextBox _panelCode = Box();
    private readonly WpfTextBox _secondaryCode = Box();
    private readonly WpfTextBox _displayName = Box();
    private readonly WpfTextBox _manufacturer = Box();
    private readonly WpfTextBox _description = Box(true);
    private readonly WpfTextBox _notes = Box(true);
    private readonly int _version;

    public PanelPackageWriteRequest Edit => new()
    {
        PixelPitch = _pitch.Text,
        PanelCode = _panelCode.Text,
        SecondaryCode = _secondaryCode.Text,
        DisplayName = EmptyToNull(_displayName.Text),
        Manufacturer = EmptyToNull(_manufacturer.Text),
        Description = EmptyToNull(_description.Text),
        Notes = EmptyToNull(_notes.Text),
        Version = _version
    };

    public PanelPackageEditWindow(PanelPackageDto package)
    {
        Title = "Paketi Düzenle";
        Width = 520;
        Height = 610;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _version = package.Version;
        _pitch.Text = package.PixelPitch;
        _panelCode.Text = package.PanelCode;
        _secondaryCode.Text = package.SecondaryCode;
        _displayName.Text = package.DisplayName;
        _manufacturer.Text = package.Manufacturer ?? "";
        _description.Text = package.Description ?? "";
        _notes.Text = package.Notes ?? "";

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = "Canonical kimlik yapılandırılmış alanlardan sunucuda yeniden üretilir.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });
        Add(panel, "Pixel Pitch", _pitch);
        Add(panel, "Panel Code", _panelCode);
        Add(panel, "Secondary Code", _secondaryCode);
        Add(panel, "Görünen ad", _displayName);
        Add(panel, "Üretici", _manufacturer);
        Add(panel, "Açıklama", _description);
        Add(panel, "Notlar", _notes);

        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var cancel = new System.Windows.Controls.Button { Content = "Vazgeç", Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(6) };
        cancel.Click += (_, _) => Close();
        var save = new System.Windows.Controls.Button { Content = "Kaydet", Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(6) };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_pitch.Text) || string.IsNullOrWhiteSpace(_panelCode.Text) || string.IsNullOrWhiteSpace(_secondaryCode.Text))
            {
                System.Windows.MessageBox.Show("Üç kimlik alanı da zorunludur.");
                return;
            }
            DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static WpfTextBox Box(bool multiline = false) => new()
    {
        MinHeight = multiline ? 68 : 30,
        TextWrapping = TextWrapping.Wrap,
        AcceptsReturn = multiline,
        Margin = new Thickness(0, 3, 0, 10)
    };

    private static void Add(WpfPanel panel, string label, UIElement input)
    {
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(input);
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
