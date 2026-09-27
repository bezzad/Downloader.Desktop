using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Downloader.Desktop.Services;
using Downloader.Desktop.Views;

namespace Downloader.Desktop.Converters;

/// <summary>
/// The line under the scheduler dial: how long the window is ("3 h 23 min window"), or that the queue
/// runs until done when there is no stop time. Values: start time, stop time, <see cref="Localizer.Tick"/>
/// (bound only so the text follows a language switch).
/// </summary>
public sealed class ScheduleWindowConverter : IMultiValueConverter
{
    public static readonly ScheduleWindowConverter Instance = new();

    public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[1] is not TimeSpan stop)
            return Localizer.Instance["Sched_UntilDone"];
        int start = TimeDial.ToMinutes(values[0] as TimeSpan?);
        int window = TimeDial.WindowMinutes(start, TimeDial.ToMinutes(stop));
        return string.Format(Localizer.Instance["Sched_Window"], window / 60, window % 60);
    }
}
