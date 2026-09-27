using System;
using Avalonia;

namespace Downloader.Desktop.Views;

/// <summary>
/// Pure geometry of the 24-hour scheduler dial (<see cref="TimeRangeDial"/>): midnight sits at the top and
/// the day runs clockwise, one minute per 1/1440 of a turn. Kept free of any control state so the mapping
/// between a pointer position and a time of day is unit-testable.
/// </summary>
public static class TimeDial
{
    public const int MinutesPerDay = 24 * 60;

    /// <summary>Wraps any minute count into a time of day, 0..1439.</summary>
    public static int Normalize(int minutes) => ((minutes % MinutesPerDay) + MinutesPerDay) % MinutesPerDay;

    /// <summary>A stored time as minutes past midnight; null reads as midnight.</summary>
    public static int ToMinutes(TimeSpan? time) =>
        time is { } t ? Normalize((int)Math.Round(t.TotalMinutes)) : 0;

    public static TimeSpan ToTime(int minutes) => TimeSpan.FromMinutes(Normalize(minutes));

    /// <summary>The screen angle (radians, y pointing down) of a time: midnight is straight up.</summary>
    public static double AngleOf(int minutes) => Normalize(minutes) * 2 * Math.PI / MinutesPerDay - Math.PI / 2;

    public static Point PointAt(Point center, double radius, int minutes)
    {
        double a = AngleOf(minutes);
        return new Point(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a));
    }

    /// <summary>The time under an offset from the dial's center, to the nearest minute.</summary>
    public static int MinutesAt(double dx, double dy)
    {
        double a = Math.Atan2(dy, dx) + Math.PI / 2;
        if (a < 0) a += 2 * Math.PI;
        return Normalize((int)Math.Round(a * MinutesPerDay / (2 * Math.PI)));
    }

    /// <summary>Length of the window from start to stop, wrapping past midnight (22:00 → 02:00 is 4 h).</summary>
    public static int WindowMinutes(int start, int stop) => Normalize(stop - start);

    public static string Format(int minutes)
    {
        int m = Normalize(minutes);
        return $"{m / 60:00}:{m % 60:00}";
    }
}
