using System;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// A dispatcher timer tick must never let an exception escape.
/// <para>
/// An unhandled exception in a <c>DispatcherTimer</c> handler takes down the thread running the
/// dispatcher. In the app that is the UI thread — the window simply stops responding. In the headless
/// test suite it takes the shared dispatcher with it, so every later test waits for a dispatcher that no
/// longer exists and not even the per-test timeout can fire, because there is nothing left to run it.
/// That is what a CI hang looked like: a three-minute inactivity abort blamed on whichever unrelated
/// test happened to be next, with no thread anywhere in the dump executing app code.
/// </para>
/// </summary>
public class TimerTickSafetyTests
{
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_throwing_stats_listener_cannot_take_the_pump_tick_down()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        // The pump raises StatsChanged; a listener is app code that can fail for its own reasons.
        manager.StatsChanged += () => throw new InvalidOperationException("boom");

        // The tick must absorb it. Before the guard this threw straight into the dispatcher loop.
        manager.RunUiPumpTickOnce();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_throwing_list_listener_cannot_take_the_scheduler_tick_down()
    {
        var manager = new DownloadManager();
        var cfg = Config.New();
        cfg.Schedules.Add(new DownloadSchedule { Enabled = true, TargetQueueId = "no-such-queue" });
        manager.Initialize(cfg);
        manager.ListChanged += () => throw new InvalidOperationException("boom");

        // EvaluateSchedules is what the scheduler timer calls; the same guard wraps it there.
        manager.EvaluateSchedules();
    }
}
