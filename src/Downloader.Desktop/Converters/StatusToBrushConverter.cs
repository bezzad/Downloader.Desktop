using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Downloader;

namespace Downloader.Desktop.Converters;

/// <summary>Maps a <see cref="DownloadStatus"/> to a badge background brush.</summary>
public class StatusToBrushConverter : IValueConverter
{
    public static readonly StatusToBrushConverter Instance = new();

    private static readonly IBrush Running = new SolidColorBrush(Color.Parse("#0E8FB3"));
    private static readonly IBrush Completed = new SolidColorBrush(Color.Parse("#1FA971"));
    private static readonly IBrush Failed = new SolidColorBrush(Color.Parse("#E5594F"));
    private static readonly IBrush Paused = new SolidColorBrush(Color.Parse("#E0922F"));
    private static readonly IBrush Stopped = new SolidColorBrush(Color.Parse("#6B7A83"));
    private static readonly IBrush Queued = new SolidColorBrush(Color.Parse("#7C8A93"));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        DownloadStatus.Running => Running,
        DownloadStatus.Completed => Completed,
        DownloadStatus.Failed => Failed,
        DownloadStatus.Paused => Paused,
        DownloadStatus.Stopped => Stopped,
        _ => Queued
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
