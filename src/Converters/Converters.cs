using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VideoDownloaderPro.Models;

namespace VideoDownloaderPro.Converters;

/// <summary>Converts boolean to Visibility.</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c)
    {
        // Support string equality check: value is current tab, parameter is target tab
        if (value is string current && parameter is string target)
            return current == target ? Visibility.Visible : Visibility.Collapsed;
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object parameter, CultureInfo c)
        => value is Visibility.Visible;
}

/// <summary>Inverse boolean to Visibility.</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c)
    {
        if (value is string current && parameter is string target)
            return current != target ? Visibility.Visible : Visibility.Collapsed;
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }
    public object ConvertBack(object value, Type t, object parameter, CultureInfo c)
        => value is Visibility.Collapsed;
}

/// <summary>Converts DownloadStatus to a color brush.</summary>
public class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c)
    {
        if (value is DownloadStatus status)
        {
            return status switch
            {
                DownloadStatus.Downloading => new SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246)),
                DownloadStatus.Completed => new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)),
                DownloadStatus.Failed => new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)),
                DownloadStatus.Paused => new SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 179, 8)),
                DownloadStatus.Cancelled => new SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175)),
                _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)),
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }
    public object ConvertBack(object value, Type t, object parameter, CultureInfo c)
        => throw new NotImplementedException();
}

/// <summary>Converts progress (0-100) to width fraction.</summary>
public class ProgressToWidthConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c)
    {
        if (value is double d) return Math.Max(0, Math.Min(100, d)) / 100.0;
        return 0.0;
    }
    public object ConvertBack(object value, Type t, object parameter, CultureInfo c)
        => throw new NotImplementedException();
}

/// <summary>Formats view count with commas.</summary>
public class ViewCountConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c)
    {
        if (value is long v && v > 0) return $"{v:N0} views";
        return "";
    }
    public object ConvertBack(object value, Type t, object parameter, CultureInfo c)
        => throw new NotImplementedException();
}

/// <summary>Converts DownloadStatus to bool for button states.</summary>
public class StatusToBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c)
    {
        if (value is DownloadStatus status && parameter is string param)
        {
            return param switch
            {
                "CanDownload" => status is DownloadStatus.Ready or DownloadStatus.Queued
                    or DownloadStatus.Failed or DownloadStatus.Cancelled or DownloadStatus.Completed,
                "CanPause" => status == DownloadStatus.Downloading,
                "CanResume" => status == DownloadStatus.Paused,
                "CanCancel" => status is DownloadStatus.Downloading or DownloadStatus.Paused,
                _ => false
            };
        }
        return false;
    }
    public object ConvertBack(object value, Type t, object parameter, CultureInfo c)
        => throw new NotImplementedException();
}

/// <summary>Converts non-empty string to Visibility.Visible.</summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c)
    {
        return !string.IsNullOrWhiteSpace(value as string) ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object parameter, CultureInfo c)
        => throw new NotImplementedException();
}
