using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Downloader.Desktop.Converters;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The scheduler's 24-hour dial driven through real pointer and keyboard input: the dial is hosted at
/// 220×220, so its center is (110,110) and the ring radius is 98.
/// </summary>
public class TimeRangeDialTests
{
    private static readonly Point Center = new(110, 110);
    private const double Ring = 98;

    private static (Window window, TimeRangeDial dial) Host(TimeSpan? start, TimeSpan? stop)
    {
        var dial = new TimeRangeDial { StartTime = start, StopTime = stop };
        var window = new Window { Width = 220, Height = 220, Content = dial };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, dial);
    }

    private static Point At(int minutes) => TimeDial.PointAt(Center, Ring, minutes);

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Pressing_the_ring_moves_the_start_handle_there()
    {
        var (window, dial) = Host(new TimeSpan(1, 0, 0), null);

        window.MouseDown(At(6 * 60), MouseButton.Left);
        window.MouseUp(At(6 * 60), MouseButton.Left);

        Assert.Equal(new TimeSpan(6, 0, 0), dial.StartTime);
        Assert.Null(dial.StopTime);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_right_to_left_window_does_not_mirror_the_clock()
    {
        var dial = new TimeRangeDial { StartTime = new TimeSpan(1, 0, 0) };
        var window = new Window
        {
            Width = 220, Height = 220, Content = dial, FlowDirection = Avalonia.Media.FlowDirection.RightToLeft
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The right of the screen is still 06:00 (a mirrored dial reads it as 18:00).
        window.MouseDown(At(6 * 60), MouseButton.Left);
        window.MouseUp(At(6 * 60), MouseButton.Left);

        Assert.Equal(new TimeSpan(6, 0, 0), dial.StartTime);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Dragging_reaches_a_single_minute()
    {
        var (window, dial) = Host(new TimeSpan(18, 0, 0), null);

        window.MouseDown(At(18 * 60), MouseButton.Left);
        window.MouseMove(At(18 * 60 + 20));
        window.MouseMove(At(18 * 60 + 37));
        window.MouseUp(At(18 * 60 + 37), MouseButton.Left);

        Assert.Equal(new TimeSpan(18, 37, 0), dial.StartTime);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_press_moves_whichever_handle_is_nearer()
    {
        var (window, dial) = Host(new TimeSpan(1, 0, 0), new TimeSpan(7, 0, 0));

        // 12:00 (bottom) is far nearer to the 07:00 stop handle than to the 01:00 start handle.
        window.MouseDown(At(12 * 60), MouseButton.Left);
        window.MouseUp(At(12 * 60), MouseButton.Left);

        Assert.Equal(new TimeSpan(1, 0, 0), dial.StartTime);
        Assert.Equal(new TimeSpan(12, 0, 0), dial.StopTime);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Arrow_keys_step_the_last_used_handle_by_a_minute_and_shift_by_fifteen()
    {
        var (window, dial) = Host(new TimeSpan(1, 0, 0), new TimeSpan(7, 0, 0));
        window.MouseDown(At(7 * 60), MouseButton.Left);   // grab the stop handle where it already is
        window.MouseUp(At(7 * 60), MouseButton.Left);

        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, "");
        Assert.Equal(new TimeSpan(7, 1, 0), dial.StopTime);

        window.KeyPress(Key.Left, RawInputModifiers.Shift, PhysicalKey.ArrowLeft, "");
        Assert.Equal(new TimeSpan(6, 46, 0), dial.StopTime);
        Assert.Equal(new TimeSpan(1, 0, 0), dial.StartTime);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Stepping_back_past_midnight_wraps_to_the_previous_evening()
    {
        var (window, dial) = Host(TimeSpan.Zero, null);
        window.MouseDown(At(0), MouseButton.Left);
        window.MouseUp(At(0), MouseButton.Left);

        window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, "");

        Assert.Equal(new TimeSpan(23, 59, 0), dial.StartTime);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_stop_switch_opens_a_one_hour_window_and_closing_it_clears_the_stop()
    {
        var schedule = new DownloadSchedule { StartTime = new TimeSpan(23, 30, 0) };
        var row = new ScheduleRowViewModel(schedule, Config.New(), null);
        Assert.False(row.HasStopTime);
        Assert.Equal("23:30", row.RangeText);
        Assert.Equal("—", row.StopText);

        row.HasStopTime = true;
        Assert.Equal(new TimeSpan(0, 30, 0), schedule.StopTime);   // one hour later, wrapping past midnight
        Assert.Equal("23:30 – 00:30", row.RangeText);

        row.HasStopTime = false;
        Assert.Null(schedule.StopTime);
        Assert.Equal("23:30", row.RangeText);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_new_schedule_starts_at_the_next_whole_hour_with_a_one_hour_window()
    {
        var config = Config.New();
        var page = new SchedulerViewModel(config, null);
        int before = DateTime.Now.Hour;

        page.NewScheduleCommand.Execute(null);

        var schedule = Assert.Single(config.Schedules);
        TimeSpan start = schedule.StartTime;
        Assert.Equal(0, start.Minutes);
        Assert.Equal(0, start.Seconds);
        // The next hour after "now" (either side of an hour boundary crossed during the test).
        Assert.Contains(start.Hours, new[] { (before + 1) % 24, (before + 2) % 24 });
        Assert.NotNull(schedule.StopTime);
        Assert.Equal(60, TimeDial.WindowMinutes(TimeDial.ToMinutes(start), TimeDial.ToMinutes(schedule.StopTime)));
        Assert.True(page.Schedules[0].HasStopTime);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_line_under_the_dial_says_how_long_the_window_is()
    {
        Localizer.Instance.Load("en");
        var convert = ScheduleWindowConverter.Instance;

        Assert.Equal("3 h 23 min window", convert.Convert(
            new object[] { new TimeSpan(18, 37, 0), new TimeSpan(22, 0, 0), 0 }, typeof(string), null, null));
        Assert.Equal("Runs until done", convert.Convert(
            new object[] { new TimeSpan(18, 37, 0), null, 0 }, typeof(string), null, null));
    }
}
