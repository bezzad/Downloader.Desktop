using System;
using Avalonia;
using Downloader.Desktop.Views;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>The scheduler dial's geometry: midnight at the top, clockwise, one minute per 1/1440 turn.</summary>
public class TimeDialTests
{
    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(0, -1, 0)]       // straight up = midnight
    [InlineData(1, 0, 360)]      // right = 06:00
    [InlineData(0, 1, 720)]      // down = 12:00
    [InlineData(-1, 0, 1080)]    // left = 18:00
    public void The_compass_points_are_the_quarter_days(double dx, double dy, int expected)
    {
        Assert.Equal(expected, TimeDial.MinutesAt(dx, dy));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Every_minute_of_the_day_can_be_reached_and_read_back_exactly()
    {
        var center = new Point(110, 110);
        for (int m = 0; m < TimeDial.MinutesPerDay; m++)
        {
            Point p = TimeDial.PointAt(center, 98, m);
            Assert.Equal(m, TimeDial.MinutesAt(p.X - center.X, p.Y - center.Y));
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_window_that_crosses_midnight_is_measured_forward()
    {
        Assert.Equal(240, TimeDial.WindowMinutes(22 * 60, 2 * 60));
        Assert.Equal(203, TimeDial.WindowMinutes(18 * 60 + 37, 22 * 60));
        Assert.Equal(0, TimeDial.WindowMinutes(300, 300));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Times_wrap_into_one_day_and_format_as_24_hour()
    {
        Assert.Equal(1439, TimeDial.Normalize(-1));
        Assert.Equal(1, TimeDial.Normalize(TimeDial.MinutesPerDay + 1));
        Assert.Equal("00:00", TimeDial.Format(TimeDial.MinutesPerDay));
        Assert.Equal("18:37", TimeDial.Format(18 * 60 + 37));
        Assert.Equal(0, TimeDial.ToMinutes(null));
        Assert.Equal(TimeSpan.FromMinutes(1439), TimeDial.ToTime(-1));
    }
}
