using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

public class NotchTests
{
    private static (DownloadManager manager, Config config) NewManager()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false;
        manager.Initialize(config);
        return (manager, config);
    }

    private static DownloadItemViewModel AddRunning(DownloadManager m, string name)
    {
        var vm = m.Add(new DownloadItem { Urls = { $"https://h/{name}" }, FileName = name }, autoStart: false);
        vm.Status = global::Downloader.DownloadStatus.Running;
        return vm;
    }

    [AvaloniaFact]
    public void Notch_vm_lists_top_rows_with_overflow_and_total_speed()
    {
        var (manager, _) = NewManager();
        for (var i = 0; i < 5; i++)
        {
            var vm = AddRunning(manager, $"file{i}.zip");
            vm.Speed = 1024 * 1024; // 1 MB/s each
        }

        using var notch = new NotchViewModel(manager);
        Assert.Equal(NotchViewModel.MaxRows, notch.RunningRows.Count);
        Assert.True(notch.HasRows);
        Assert.True(notch.HasOverflow);
        Assert.Contains("2", notch.OverflowText); // 5 running − 3 listed
        Assert.True(notch.HasActivity);
        Assert.StartsWith("↓", notch.TotalSpeedText);
        Assert.Matches(@"\d", notch.TimeText); // live clock text
    }

    [AvaloniaFact]
    public void Notch_vm_is_quiet_when_idle()
    {
        var (manager, _) = NewManager();
        using var notch = new NotchViewModel(manager);
        Assert.False(notch.HasRows);
        Assert.False(notch.HasOverflow);
        Assert.False(notch.HasActivity); // no speed chip in the pill when nothing runs
    }

    [AvaloniaFact]
    public void Notch_window_builds_and_toggles_expanded_state()
    {
        var (manager, _) = NewManager();
        AddRunning(manager, "movie.mkv").Speed = 512 * 1024;

        var vm = new NotchViewModel(manager);
        var view = new NotchView { DataContext = vm };
        view.Show();
        try
        {
            Assert.False(vm.IsExpanded);
            // The window never participates in the taskbar or focus stealing.
            Assert.False(view.ShowInTaskbar);
            Assert.True(view.Topmost);

            vm.IsExpanded = true; // content swap is binding-driven
            Assert.True(vm.IsExpanded);
        }
        finally { view.Close(); vm.Dispose(); }
    }

    /// <summary>Gated mockup capture for the author's visual review (task 1.1 of the notch change):
    /// DLDESKTOP_NOTCH_MOCKUP=1 renders collapsed + expanded PNGs into the openspec change folder.</summary>
    [AvaloniaFact]
    public void CaptureNotchMockups()
    {
        if (Environment.GetEnvironmentVariable("DLDESKTOP_NOTCH_MOCKUP") != "1")
            return;

        var (manager, _) = NewManager();
        var a = AddRunning(manager, "skate_phantom_flex_4k.mp4"); a.Speed = 4.2 * 1024 * 1024; a.Progress = 62;
        var b = AddRunning(manager, "gemma3-12b.gguf"); b.Speed = 8.8 * 1024 * 1024; b.Progress = 31;
        var c = AddRunning(manager, "ubuntu-24.04.iso"); c.Speed = 2.1 * 1024 * 1024; c.Progress = 87;
        var d = AddRunning(manager, "podcast-ep12.mp3"); d.Speed = 0; d.Progress = 45;
        d.Status = global::Downloader.DownloadStatus.Paused; // a paused row shows "45% · Paused"

        var outDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..", "openspec", "changes", "add-dynamic-island-notch"));
        Directory.CreateDirectory(outDir);

        var vm = new NotchViewModel(manager);
        var view = new NotchView { DataContext = vm };
        view.Show();
        try
        {
            for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            view.CaptureRenderedFrame()!.Save(Path.Combine(outDir, "mockup-collapsed.png"));

            vm.IsExpanded = true;
            view.Width = NotchView.ExpandedWidth; view.Height = NotchView.ExpandedHeight;
            for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            view.CaptureRenderedFrame()!.Save(Path.Combine(outDir, "mockup-expanded.png"));
        }
        finally { view.Close(); vm.Dispose(); }
    }

    [AvaloniaFact]
    public void Notch_service_starts_and_stops_fail_soft()
    {
        var (manager, _) = NewManager();
        try
        {
            NotchService.Start(manager);
            Assert.True(NotchService.IsActive);
            NotchService.Start(manager); // idempotent
            Assert.True(NotchService.IsActive);
        }
        finally
        {
            NotchService.Stop();
            Assert.False(NotchService.IsActive);
        }
    }
}
