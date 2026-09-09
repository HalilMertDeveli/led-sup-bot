using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace LedSupBot.Desktop.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is true;
        if (parameter?.ToString() == "Inverse")
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasText = !string.IsNullOrWhiteSpace(value?.ToString());
        if (parameter?.ToString() == "Inverse")
        {
            hasText = !hasText;
        }

        return hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ConnectionBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true
            ? (SolidColorBrush)System.Windows.Application.Current.FindResource("SuccessBrush")
            : (SolidColorBrush)System.Windows.Application.Current.FindResource("DangerBrush");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BotModeBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() switch
        {
            "Bot" => (SolidColorBrush)System.Windows.Application.Current.FindResource("SuccessBrush"),
            "Paused" => (SolidColorBrush)System.Windows.Application.Current.FindResource("WarnBrush"),
            "Human" => (SolidColorBrush)System.Windows.Application.Current.FindResource("InfoBrush"),
            _ => (SolidColorBrush)System.Windows.Application.Current.FindResource("CardMutedBrush"),
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BotModeBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() switch
        {
            "Bot" => (SolidColorBrush)System.Windows.Application.Current.FindResource("BadgeSuccessBgBrush"),
            "Paused" => (SolidColorBrush)System.Windows.Application.Current.FindResource("BadgeWarnBgBrush"),
            "Human" => (SolidColorBrush)System.Windows.Application.Current.FindResource("BadgeInfoBgBrush"),
            _ => (SolidColorBrush)System.Windows.Application.Current.FindResource("SurfaceBrush"),
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// CRITICAL REQUIREMENT:
/// All outbound messages visible in the UI must appear as messages sent by "Sen".
/// Do NOT display BOT, AI, TECHNICIAN, SentBy, or botOriginated to the operator.
/// </summary>
public sealed class MessageSenderLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var role = value?.ToString()?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(role) || role is "technician" or "bot" or "outbound" or "system")
        {
            return "Sen";
        }

        return "Müşteri";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class MessageAlignmentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var role = value?.ToString()?.Trim().ToLowerInvariant();
        var isOutbound = string.IsNullOrWhiteSpace(role) || role is "technician" or "bot" or "outbound";
        return isOutbound ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class MessageBubbleBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var role = value?.ToString()?.Trim().ToLowerInvariant();
        var isOutbound = string.IsNullOrWhiteSpace(role) || role is "technician" or "bot" or "outbound";
        return isOutbound
            ? (SolidColorBrush)System.Windows.Application.Current.FindResource("MsgOutboundBgBrush")
            : (SolidColorBrush)System.Windows.Application.Current.FindResource("MsgInboundBgBrush");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class MessageBubbleBorderConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var role = value?.ToString()?.Trim().ToLowerInvariant();
        var isOutbound = string.IsNullOrWhiteSpace(role) || role is "technician" or "bot" or "outbound";
        return isOutbound
            ? (SolidColorBrush)System.Windows.Application.Current.FindResource("MsgOutboundBorderBrush")
            : (SolidColorBrush)System.Windows.Application.Current.FindResource("MsgInboundBorderBrush");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class MessageSenderBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var role = value?.ToString()?.Trim().ToLowerInvariant();
        var isOutbound = string.IsNullOrWhiteSpace(role) || role is "technician" or "bot" or "outbound";
        return isOutbound
            ? (SolidColorBrush)System.Windows.Application.Current.FindResource("MsgOutboundSenderBrush")
            : (SolidColorBrush)System.Windows.Application.Current.FindResource("MsgInboundSenderBrush");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RelativeTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTimeOffset dto && value is not DateTime dt)
        {
            return "";
        }

        var time = value is DateTimeOffset offset ? offset.ToLocalTime() : new DateTimeOffset((DateTime)value).ToLocalTime();
        var now = DateTimeOffset.Now;
        var diff = now - time;

        if (time.Date == now.Date)
        {
            return time.ToString("HH:mm");
        }

        if (time.Date == now.Date.AddDays(-1))
        {
            return "Dün " + time.ToString("HH:mm");
        }

        if (time.Year == now.Year)
        {
            return time.ToString("dd MMM HH:mm", new CultureInfo("tr-TR"));
        }

        return time.ToString("dd.MM.yyyy HH:mm");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class DeliveryStatusIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value?.ToString()?.Trim().ToLowerInvariant();
        return status switch
        {
            "delivered" or "2" => "✓✓",
            "read" or "3" => "✓✓",
            "sent" or "1" => "✓",
            "pending" or "0" => "⏳",
            "failed" or "4" => "⚠",
            _ => "",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StepStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value?.ToString()?.Trim().ToLowerInvariant();
        return status switch
        {
            "completed" or "tamam" or "true" => (SolidColorBrush)System.Windows.Application.Current.FindResource("SuccessBrush"),
            "active" or "inprogress" or "bekleniyor" => (SolidColorBrush)System.Windows.Application.Current.FindResource("WarnBrush"),
            _ => (SolidColorBrush)System.Windows.Application.Current.FindResource("CardMutedBrush"),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StepStatusToBgBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value?.ToString()?.Trim().ToLowerInvariant();
        return status switch
        {
            "completed" or "tamam" or "true" => (SolidColorBrush)System.Windows.Application.Current.FindResource("BadgeSuccessBgBrush"),
            "active" or "inprogress" or "bekleniyor" => (SolidColorBrush)System.Windows.Application.Current.FindResource("BadgeWarnBgBrush"),
            _ => (SolidColorBrush)System.Windows.Application.Current.FindResource("SurfaceBrush"),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = 0;
        if (value is int i) count = i;
        else if (value is long l) count = (int)l;

        return count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
