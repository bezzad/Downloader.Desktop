using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// Regression coverage for "stop all downloads, restart the app, they start downloading again on their
/// own" (fix-ux-reliability-batch). Root cause traced to the scheduler re-firing an already-fired-today
/// start trigger on the very first tick after a fresh launch, because its "did this already fire" tracking
/// was in-memory only and reset every process start. Uses an unreachable test IP (10.255.255.1) for any
/// item that might actually get started, matching the existing `Start_runs_item_even_when_its_queue_was_paused`
/// pattern — <c>Start</c> flips <c>Status</c> to Running synchronously before its first await, so the test
/// observes the state transition without waiting on (or depending on) real network I/O.
/// </summary>
public class DownloadStateDurabilityTests
{
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void StopAll_survives_a_restart_with_no_schedules_configured()
    {
        // Session 1: add a few items, some running, some already queued, then Stop All.
        var manager1 = new DownloadManager();
        var config = Config.New();
        manager1.Initialize(config);
        var a = manager1.Add(new DownloadItem { Url = "https://10.255.255.1/a.zip", SaveFolder = "/tmp" }, autoStart: true);
        var b = manager1.Add(new DownloadItem { Url = "https://10.255.255.1/b.zip", SaveFolder = "/tmp" }, autoStart: false);
        manager1.StopAll();

        Assert.Equal(DownloadStatus.Stopped, a.Status);
        Assert.Equal(DownloadStatus.Stopped, b.Status);

        // Session 2: a fresh DownloadManager over the SAME (persisted) config, simulating a restart.
        // config.Downloads is only synced by the real save path (MainViewModel.SaveConfigFile); do the same
        // snapshot here so Initialize has something to load, matching what actually hits disk.
        config.Downloads = manager1.Items.Select(vm => vm.GetItem()).ToList();
        var manager2 = new DownloadManager();
        manager2.Initialize(config);

        Assert.All(manager2.Items, vm => Assert.Equal(DownloadStatus.Stopped, vm.Status));
        Assert.DoesNotContain(manager2.Items, vm => vm.Status is DownloadStatus.Running or DownloadStatus.Created or DownloadStatus.None);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Schedule_that_already_fired_today_does_not_refire_on_restart()
    {
        var manager1 = new DownloadManager();
        var config = Config.New();
        manager1.Initialize(config);
        var vm = manager1.Add(new DownloadItem { Url = "https://10.255.255.1/a.zip", SaveFolder = "/tmp" }, autoStart: false);
        manager1.StopAll();
        Assert.Equal(DownloadStatus.Stopped, vm.Status);

        // An always-in-window schedule (StartTime = midnight, no StopTime) that already fired earlier today.
        config.Schedules.Add(new DownloadSchedule
        {
            Enabled = true,
            TargetQueueId = config.DefaultQueue.Id,
            StartTime = TimeSpan.Zero,
            StopTime = null,
            LastFiredStartDate = DateTime.Now.Date,
        });

        // Session 2: fresh manager, first scheduler tick after "launch" (mirrors StartScheduler's first tick).
        config.Downloads = manager1.Items.Select(v => v.GetItem()).ToList();
        var manager2 = new DownloadManager();
        manager2.Initialize(config);
        manager2.EvaluateSchedules();

        var vm2 = manager2.Items.Single();
        Assert.Equal(DownloadStatus.Stopped, vm2.Status);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Schedule_that_has_not_fired_today_still_fires_normally()
    {
        // The "catch-up" case this fix must NOT break: a schedule that legitimately hasn't fired yet today
        // (fresh day, or the very first evaluation ever) should still start its target queue.
        var manager1 = new DownloadManager();
        var config = Config.New();
        manager1.Initialize(config);
        var vm = manager1.Add(new DownloadItem { Url = "https://10.255.255.1/a.zip", SaveFolder = "/tmp" }, autoStart: false);
        manager1.StopAll();

        var schedule = new DownloadSchedule
        {
            Enabled = true,
            TargetQueueId = config.DefaultQueue.Id,
            StartTime = TimeSpan.Zero,
            StopTime = null,
            LastFiredStartDate = null, // never fired
        };
        config.Schedules.Add(schedule);

        config.Downloads = manager1.Items.Select(v => v.GetItem()).ToList();
        var manager2 = new DownloadManager();
        manager2.Initialize(config);
        manager2.EvaluateSchedules();

        var vm2 = manager2.Items.Single();
        Assert.Equal(DownloadStatus.Running, vm2.Status); // StartQueue re-queued Stopped -> Created -> pumped
        Assert.Equal(DateTime.Now.Date, schedule.LastFiredStartDate); // recorded so it won't re-fire today
    }
}
