using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Image = System.Windows.Controls.Image;
using LedSupBot.Desktop.Models;
using LedSupBot.Desktop.Services;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;

namespace LedSupBot.Desktop.Views;

public sealed class WhatsAppPairingWindow : Window
{
    private readonly LedSupBotApiClient _api;
    private readonly Image _qrImage;
    private readonly TextBlock _status;
    private readonly CancellationTokenSource _closed = new();

    public WhatsAppPairingWindow(LedSupBotApiClient api)
    {
        _api = api;
        Title = "WhatsApp Yeniden Bağlama";
        Width = 470;
        Height = 590;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(15, 23, 42));

        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new TextBlock
        {
            Text = "WhatsApp bağlı cihazını yeniden eşleştirin",
            Foreground = Brushes.White,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Telefonda WhatsApp > Bağlı cihazlar > Cihaz bağla yolunu açıp QR kodunu okutun.",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(8, 0, 8, 18),
        });

        var qrBorder = new Border
        {
            Width = 360,
            Height = 360,
            Background = Brushes.White,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
        };
        _qrImage = new Image { Stretch = Stretch.Uniform };
        qrBorder.Child = _qrImage;
        panel.Children.Add(qrBorder);

        _status = new TextBlock
        {
            Text = "Yeni QR bekleniyor…",
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 16, 0, 12),
        };
        panel.Children.Add(_status);

        var close = new Button
        {
            Content = "KAPAT",
            Width = 110,
            Height = 34,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
        };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);
        Content = panel;

        Loaded += (_, _) => _ = PollQrAsync(_closed.Token);
        Closed += (_, _) => _closed.Cancel();
    }

    private async Task PollQrAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var qr = await _api.GetWhatsAppPairingQrAsync(cancellationToken);
                if (qr?.Available == true && qr.Modules.Length > 0)
                {
                    _qrImage.Source = RenderQr(qr.Modules);
                    _status.Text = "QR hazır — telefonunuzla okutun.";
                }
                else if (string.Equals(qr?.Status, "Connected", StringComparison.OrdinalIgnoreCase))
                {
                    _status.Text = "WhatsApp başarıyla bağlandı.";
                    await Task.Delay(800, cancellationToken);
                    DialogResult = true;
                    Close();
                    return;
                }
                else
                {
                    _status.Text = "Yeni QR bekleniyor…";
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                _status.Text = "Connector yanıtı bekleniyor…";
            }

            await Task.Delay(1500, cancellationToken);
        }
    }

    private static BitmapSource RenderQr(bool[][] modules)
    {
        var count = modules.Length;
        if (count == 0 || modules.Any(row => row.Length != count))
        {
            throw new InvalidOperationException("Invalid QR matrix.");
        }

        const int quiet = 4;
        const int scale = 7;
        var size = (count + quiet * 2) * scale;
        var stride = size * 4;
        var pixels = new byte[stride * size];
        Array.Fill(pixels, (byte)255);

        for (var row = 0; row < count; row++)
        for (var column = 0; column < count; column++)
        {
            if (!modules[row][column]) continue;
            for (var y = 0; y < scale; y++)
            for (var x = 0; x < scale; x++)
            {
                var offset = ((row + quiet) * scale + y) * stride + ((column + quiet) * scale + x) * 4;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = 0;
                pixels[offset + 3] = 255;
            }
        }

        return BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
    }
}
