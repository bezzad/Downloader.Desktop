using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The scheduler's 30-second <c>DispatcherTimer</c> must not outlive the manager that started it.
///
/// This is a resource bug with two victims. A real user with no schedules had the dispatcher woken every
/// 30 seconds for a list that could not fire anything. And the test suite runs at
/// <c>AvaloniaTestIsolationLevel.PerAssembly</c>, so ONE dispatcher serves the whole run: every manager a
/// test built used to leave its timer behind on that shared dispatcher. Measured on 2026-09-08 over a
/// full local run with the CI flags: <b>405</b> scheduler timers started, and ticks arriving in bursts
/// from dozens of distinct manager instances long after the tests that created them had ended — on the
/// same dispatcher the remaining tests need in order to run at all. That is the leading suspect for the
/// intermittent Windows/macOS "test host hangs with no failure and no dump" abort.
/// </summary>
public class SchedulerLifetimeTests
{
    private static Config WithSchedule()
    {
        var config = Config.New();
        config.Schedules.Add(new DownloadSchedule { Name = "nightly", Enabled = true });
        return config;
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void No_schedules_starts_no_timer()
    {
        using var manager = new DownloadManager();
        manager.Initialize(Config.New());

        Assert.False(manager.IsSchedulerRunning);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_configured_schedule_starts_the_timer()
    {
        using var manager = new DownloadManager();
        manager.Initialize(WithSchedule());

        Assert.True(manager.IsSchedulerRunning);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Adding_the_first_schedule_starts_the_timer()
    {
        using var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);
        Assert.False(manager.IsSchedulerRunning);

        config.Schedules.Add(new DownloadSchedule { Name = "nightly", Enabled = true });
        manager.SyncScheduler();

        Assert.True(manager.IsSchedulerRunning);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Removing_the_last_schedule_stops_the_timer()
    {
        using var manager = new DownloadManager();
        var config = WithSchedule();
        manager.Initialize(config);
        Assert.True(manager.IsSchedulerRunning);

        config.Schedules.Clear();
        manager.SyncScheduler();

        Assert.False(manager.IsSchedulerRunning);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Disposing_the_manager_stops_the_timer()
    {
        var manager = new DownloadManager();
        manager.Initialize(WithSchedule());
        Assert.True(manager.IsSchedulerRunning);

        manager.Dispose();

        Assert.False(manager.IsSchedulerRunning);
    }

    /// <summary>
    /// The shape that hurt the suite: many managers built and dropped within one dispatcher's lifetime.
    /// None of them may still be ticking afterwards.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Many_short_lived_managers_leave_no_timer_behind()
    {
        var managers = new DownloadManager[25];
        for (var i = 0; i < managers.Length; i++)
        {
            managers[i] = new DownloadManager();
            managers[i].Initialize(WithSchedule());
        }

        foreach (var manager in managers)
            manager.Dispose();

        Assert.DoesNotContain(managers, m => m.IsSchedulerRunning);
    }
}
