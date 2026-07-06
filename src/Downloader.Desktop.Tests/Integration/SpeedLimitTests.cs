using System.Linq;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// Regression coverage for "changing the global speed limit shouldn't clobber a per-item cap, and a
/// per-item cap must survive stop/resume and restart" (fix-ux-reliability-batch, section 4). Uses an
/// unreachable test IP so <c>Start</c> flips Status → Running and builds <c>vm.Configuration</c>
/// synchronously (before its first await) without any real network I/O — same trick as the durability tests.
/// </summary>
public class SpeedLimitTests
{
    private const long Global = 500 * 1024;
    private const long Custom = 100 * 1024;

    private static DownloadManager StartedItem(out DownloadItemViewModel vm, out Config config)
    {
        var manager = new DownloadManager();
        config = Config.New();
        manager.Initialize(config);
        vm = manager.Add(new DownloadItem { Url = "https://10.255.255.1/a.zip", SaveFolder = "/tmp" }, autoStart: true);
        Assert.Equal(DownloadStatus.Running, vm.Status);
        Assert.NotNull(vm.Configuration);
        return manager;
    }

    [AvaloniaFact]
    public void Global_change_reaches_a_running_item_without_a_custom_limit()
    {
        var manager = StartedItem(out var vm, out _);

        manager.ApplyGlobalSpeedLimit(Global);

        Assert.Equal(Global, vm.Configuration.MaximumBytesPerSecond);
    }

    [AvaloniaFact]
    public void Custom_limited_running_item_is_untouched_by_a_global_change()
    {
        var manager = StartedItem(out var vm, out _);
        vm.HasCustomSpeedLimit = true;
        vm.CustomSpeedLimitBytesPerSecond = Custom;
        vm.Configuration.MaximumBytesPerSecond = Custom;

        manager.ApplyGlobalSpeedLimit(Global);

        Assert.Equal(Custom, vm.Configuration.MaximumBytesPerSecond);
    }

    [AvaloniaFact]
    public void Custom_limit_survives_stop_resume_and_restart()
    {
        var manager1 = new DownloadManager();
        var config = Config.New();
        config.Settings.MaximumBytesPerSecond = Global; // global differs from the per-item cap
        manager1.Initialize(config);
        var vm = manager1.Add(new DownloadItem
        {
            Url = "https://10.255.255.1/a.zip",
            SaveFolder = "/tmp",
            HasCustomSpeedLimit = true,
            CustomSpeedLimitBytesPerSecond = Custom,
        }, autoStart: false);
        manager1.StopAll();
        Assert.Equal(DownloadStatus.Stopped, vm.Status);

        // Simulate a restart: fresh manager over the persisted config, then Start the item again.
        config.Downloads = manager1.Items.Select(v => v.GetItem()).ToList();
        var manager2 = new DownloadManager();
        manager2.Initialize(config);
        var vm2 = manager2.Items.Single();
        Assert.True(vm2.HasCustomSpeedLimit);

        manager2.Start(vm2);

        Assert.Equal(Custom, vm2.Configuration.MaximumBytesPerSecond); // used the custom cap, not the global
    }

    [AvaloniaFact]
    public void Reverting_to_global_reapplies_current_global_and_resubscribes()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.Settings.MaximumBytesPerSecond = Global;
        manager.Initialize(config);
        var vm = manager.Add(new DownloadItem { Url = "https://10.255.255.1/a.zip", SaveFolder = "/tmp" }, autoStart: true);
        vm.HasCustomSpeedLimit = true;
        vm.CustomSpeedLimitBytesPerSecond = Custom;
        vm.Configuration.MaximumBytesPerSecond = Custom;

        var details = new DownloadDetailsViewModel(vm);
        details.UseGlobalSpeedLimit();

        Assert.False(vm.HasCustomSpeedLimit);
        Assert.Equal(Global, vm.Configuration.MaximumBytesPerSecond); // re-applied the current global

        // Re-subscribed: a later global change now reaches this item again.
        manager.ApplyGlobalSpeedLimit(200 * 1024);
        Assert.Equal(200 * 1024, vm.Configuration.MaximumBytesPerSecond);
    }
}
