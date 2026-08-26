using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// Schedule evaluation — the timer tick that starts and stops queues at set times.
///
/// The rule that matters most is the once-per-day latch. It is persisted on the schedule
/// (LastFiredStartDate / LastFiredStopDate) rather than tracked in memory precisely because in-memory
/// tracking resets on every process start: relaunching the app inside an already-fired window looked
/// identical to "never fired today" and re-fired a start, which could undo an explicit Stop All
/// seconds after reopening the app.
///
/// Times are computed relative to "now" so the tests are independent of when the suite runs.
/// </summary>
public class SchedulerEvaluationTests
{
    private static (DownloadManager manager, Config config) Build()
    {
        Localizer.Instance.Load("en");
        var config = Config.New();
        var manager = new DownloadManager();
        manager.Initialize(config);
        config.DefaultQueue.IsRunning = false;
        return (manager, config);
    }

    private static DownloadItemViewModel Add(DownloadManager manager, string name, DownloadStatus status)
    {
        var vm = manager.Add(new DownloadItem { Url = "https://10.255.255.1/" + name, FileName = name },
            autoStart: false);
        vm.Status = status;
        return vm;
    }

    /// <summary>A window that is open right now: started an hour ago, closes in an hour.</summary>
    private static DownloadSchedule OpenWindow(string queueId) => new()
    {
        Enabled = true,
        TargetQueueId = queueId,
        StartTime = DateTime.Now.TimeOfDay - TimeSpan.FromHours(1),
        StopTime = DateTime.Now.TimeOfDay + TimeSpan.FromHours(1),
    };

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_schedule_inside_its_window_starts_the_target_queue()
    {
        var (manager, config) = Build();
        var stopped = Add(manager, "a.bin", DownloadStatus.Stopped);
        config.Schedules.Add(OpenWindow(config.DefaultQueue.Id));

        manager.EvaluateSchedules();

        Assert.True(config.DefaultQueue.IsRunning);
        Assert.NotEqual(DownloadStatus.Stopped, stopped.Status); // re-queued (and possibly started)

        manager.StopQueue(config.DefaultQueue); // don't leave connection attempts running
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_schedule_fires_its_start_only_once_a_day()
    {
        var (manager, config) = Build();
        var schedule = OpenWindow(config.DefaultQueue.Id);
        config.Schedules.Add(schedule);

        manager.EvaluateSchedules();
        Assert.Equal(DateTime.Now.Date, schedule.LastFiredStartDate);

        // Simulate the user stopping everything after the schedule fired.
        manager.StopQueue(config.DefaultQueue);
        Assert.False(config.DefaultQueue.IsRunning);

        // The 30-second tick keeps running inside the same window — it must NOT undo that Stop.
        manager.EvaluateSchedules();

        Assert.False(config.DefaultQueue.IsRunning);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_restart_inside_an_already_fired_window_does_not_re_fire()
    {
        var (manager, config) = Build();
        var schedule = OpenWindow(config.DefaultQueue.Id);
        schedule.LastFiredStartDate = DateTime.Now.Date; // persisted from earlier today
        config.Schedules.Add(schedule);

        manager.EvaluateSchedules();

        // This is the whole reason the latch is persisted rather than in-memory.
        Assert.False(config.DefaultQueue.IsRunning);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_schedule_whose_window_has_closed_pauses_the_queue()
    {
        var (manager, config) = Build();
        config.DefaultQueue.IsRunning = true;
        var running = Add(manager, "a.bin", DownloadStatus.Running);
        config.Schedules.Add(new DownloadSchedule
        {
            Enabled = true,
            TargetQueueId = config.DefaultQueue.Id,
            StartTime = DateTime.Now.TimeOfDay - TimeSpan.FromHours(2),
            StopTime = DateTime.Now.TimeOfDay - TimeSpan.FromHours(1), // already passed
        });

        manager.EvaluateSchedules();

        // The end of a scheduled window PAUSES rather than stops: the partial download keeps its
        // progress so the next window resumes it instead of starting over.
        Assert.Equal(DownloadStatus.Paused, running.Status);
        Assert.False(config.DefaultQueue.IsRunning);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_disabled_schedule_is_ignored()
    {
        var (manager, config) = Build();
        var schedule = OpenWindow(config.DefaultQueue.Id);
        schedule.Enabled = false;
        config.Schedules.Add(schedule);

        manager.EvaluateSchedules();

        Assert.False(config.DefaultQueue.IsRunning);
        Assert.Null(schedule.LastFiredStartDate);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_schedule_restricted_to_other_days_is_ignored_today()
    {
        var (manager, config) = Build();
        var schedule = OpenWindow(config.DefaultQueue.Id);
        schedule.Days = Enum.GetValues<DayOfWeek>().Where(d => d != DateTime.Now.DayOfWeek).ToArray();
        config.Schedules.Add(schedule);

        manager.EvaluateSchedules();

        Assert.False(config.DefaultQueue.IsRunning);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_schedule_listing_today_still_fires()
    {
        var (manager, config) = Build();
        var schedule = OpenWindow(config.DefaultQueue.Id);
        schedule.Days = new[] { DateTime.Now.DayOfWeek };
        config.Schedules.Add(schedule);

        manager.EvaluateSchedules();

        Assert.True(config.DefaultQueue.IsRunning);
        manager.StopQueue(config.DefaultQueue);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_run_once_schedule_disables_itself_after_firing()
    {
        var (manager, config) = Build();
        var schedule = OpenWindow(config.DefaultQueue.Id);
        schedule.Once = true;
        config.Schedules.Add(schedule);

        manager.EvaluateSchedules();

        Assert.True(config.DefaultQueue.IsRunning);
        Assert.False(schedule.Enabled); // won't come back tomorrow

        manager.StopQueue(config.DefaultQueue);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_schedule_before_its_start_time_does_nothing_yet()
    {
        var (manager, config) = Build();
        config.Schedules.Add(new DownloadSchedule
        {
            Enabled = true,
            TargetQueueId = config.DefaultQueue.Id,
            StartTime = DateTime.Now.TimeOfDay + TimeSpan.FromHours(1), // later today
            StopTime = DateTime.Now.TimeOfDay + TimeSpan.FromHours(2),
        });

        manager.EvaluateSchedules();

        Assert.False(config.DefaultQueue.IsRunning);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_schedule_pointing_at_a_deleted_queue_is_harmless()
    {
        var (manager, config) = Build();
        config.Schedules.Add(OpenWindow("a-queue-id-that-no-longer-exists"));

        manager.EvaluateSchedules(); // must not throw

        Assert.False(config.DefaultQueue.IsRunning);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Evaluating_with_no_schedules_is_a_no_op()
    {
        var (manager, config) = Build();

        manager.EvaluateSchedules();

        Assert.Empty(config.Schedules);
        Assert.False(config.DefaultQueue.IsRunning);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_schedule_can_target_a_single_download_instead_of_a_queue()
    {
        var (manager, config) = Build();
        var item = Add(manager, "a.bin", DownloadStatus.Stopped);
        var schedule = OpenWindow(null);
        schedule.TargetItemId = item.GetItem().Id;
        config.Schedules.Add(schedule);

        manager.EvaluateSchedules();

        Assert.NotEqual(DownloadStatus.Stopped, item.Status);

        manager.StopAll();
    }
}
