using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Downloader;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;
using Xunit;

namespace Downloader.Desktop.Tests;

/// <summary>
/// Renders the app with sample data and saves PNG screenshots for the README.
/// Skipped during normal test runs; enable with the DLDESKTOP_CAPTURE=1 env var.
/// </summary>
public class CaptureScreenshots
{
    private static readonly string OutDir = Path.Combine(FindRepoRoot(), "docs", "screenshots");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Downloader.Desktop.sln")))
            dir = dir.Parent;
        return dir?.Parent?.FullName ?? throw new DirectoryNotFoundException("Could not locate repo root (Downloader.Desktop.sln not found in any parent directory).");
    }

    private sealed class SampleFileService : IFileService
    {
        private readonly Config _config;
        public SampleFileService(Config config) => _config = config;
        public Task<Config> LoadFromFileAsync() => Task.FromResult(_config);
        public Task SaveToFileAsync(Config itemToSave) => Task.CompletedTask;
    }

    private static Config SampleConfig()
    {
        var config = Config.New();
        config.Settings.DefaultSavePath = "/home/user/Downloads";
        config.Downloads.Add(Item("ubuntu-24.04.2-desktop.iso", 5_100_000_000, 3_162_000_000, DownloadStatus.Running));
        config.Downloads.Add(Item("interstellar-trailer.mp4", 240_000_000, 74_400_000, DownloadStatus.Running));
        config.Downloads.Add(Item("the-daily-podcast-ep12.mp3", 52_000_000, 18_200_000, DownloadStatus.Paused));
        config.Downloads.Add(Item("annual-report-2025.pdf", 12_400_000, 12_400_000, DownloadStatus.Completed));
        var failed = Item("project-photos.zip", 340_000_000, 41_000_000, DownloadStatus.Failed);
        failed.LastError = "Network error: the remote host could not be reached.";
        config.Downloads.Add(failed);

        // Sample schedules so the Scheduler page renders populated cards.
        config.Schedules.Add(new DownloadSchedule
        {
            Name = "Overnight downloads",
            TargetQueueId = config.DefaultQueue.Id,
            StartTime = new TimeSpan(1, 0, 0),
            StopTime = new TimeSpan(7, 0, 0),
            Enabled = true
        });
        config.Schedules.Add(new DownloadSchedule
        {
            Name = "Evening catch-up",
            TargetQueueId = config.DefaultQueue.Id,
            StartTime = new TimeSpan(20, 30, 0),
            Once = true,
            Enabled = false
        });
        return config;
    }

    private static DownloadItem Item(string name, long size, long got, DownloadStatus status) => new()
    {
        Url = "https://example.com/files/" + name,
        SaveFolder = "/home/user/Downloads",
        FileName = name,
        Size = size,
        Downloaded = got,
        Status = status,
        LastTry = DateTime.Now
    };

    private static void Pump(int times = 8)
    {
        for (var i = 0; i < times; i++)
            Dispatcher.UIThread.RunJobs();
    }

    private static void Save(Avalonia.Controls.Window window, string file)
    {
        Pump();
        var frame = window.CaptureRenderedFrame();
        Directory.CreateDirectory(OutDir);
        frame!.Save(Path.Combine(OutDir, file));
    }

    [AvaloniaFact]
    public void Capture()
    {
        if (Environment.GetEnvironmentVariable("DLDESKTOP_CAPTURE") != "1")
            return;

        var manager = new DownloadManager();
        var vm = new MainViewModel(new SampleFileService(SampleConfig()), manager);
        Pump();

        // Loaded items are normalized to Stopped; show the first two as actively downloading.
        foreach (var row in manager.Items.Take(2))
        {
            row.Status = DownloadStatus.Running;
            row.Speed = 8_400_000;
        }

        var window = new MainWindow { DataContext = vm };
        window.Show();

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        Save(window, "home-dark.png");

        // VERIFY (#row-select): programmatically select a row and capture it so the selected-row text
        // contrast can be eyeballed in both themes (a headless click only reads as hover, not selection).
        Pump();
        var grid = window.GetVisualDescendants().OfType<Avalonia.Controls.DataGrid>().FirstOrDefault();
        if (grid != null)
        {
            grid.SelectedIndex = 1;
            Save(window, "home-selected-dark.png");
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            Save(window, "home-selected-light.png");
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            grid.SelectedIndex = -1;
        }

        // VERIFY: click a cell and confirm no per-cell focus/current border appears (#3/#8).
        Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, new Avalonia.Point(360, 240), Avalonia.Input.MouseButton.Left);
        Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, new Avalonia.Point(360, 240), Avalonia.Input.MouseButton.Left);
        Save(window, "verify-cellclick.png");

        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        Save(window, "home-light.png");

        // Settings page (dark + light, so the README can be theme-aware)
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        vm.ShowSettingViewCommand.Execute(null);
        Save(window, "settings-dark.png");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        Save(window, "settings-light.png");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        // Settings scrolled to the new Language(flag) + Theme + Accent controls so the accent picker and
        // the country flag are actually visible in docs (they sit below the fold in the top-of-page shot).
        Pump();
        var settingsView = window.GetVisualDescendants().OfType<Downloader.Desktop.Views.SettingView>().FirstOrDefault();
        var sv = settingsView?.GetVisualDescendants().OfType<Avalonia.Controls.ScrollViewer>().FirstOrDefault();
        if (sv != null)
        {
            sv.Offset = new Avalonia.Vector(0, 215);
            Save(window, "settings-accent-dark.png");
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            Save(window, "settings-accent-light.png");
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            sv.Offset = default;
        }

        // Queues page (real queue manager: aggregate stats + per-item progress/actions).
        vm.ShowQueuesCommand.Execute(null);
        Save(window, "queues-dark.png");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        Save(window, "queues-light.png");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        // Scheduler page (daily start/stop rules per queue).
        vm.ShowSchedulerCommand.Execute(null);
        Save(window, "scheduler-dark.png");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        Save(window, "scheduler-light.png");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        vm.ShowSettingViewCommand.Execute(null); // leave a known page before the RTL capture
        vm.ShowAllCommand.Execute(null);

        // Persian (RTL) home to verify translation + right-to-left mirroring.
        Localizer.Instance.Load("fa");
        vm.ShowAllCommand.Execute(null);
        Save(window, "home-fa-dark.png");
        Localizer.Instance.Load("en");

        // Details window (dark) — needs a live Configuration so the speed-limit numeric shows.
        var detItem = manager.Items.First();
        detItem.Configuration = new DownloadConfiguration { MaximumBytesPerSecond = 512 * 1024 };
        var det = new DownloadDetailsView { DataContext = new DownloadDetailsViewModel(detItem) };
        det.Show();
        Save(det, "details-dark.png");
    }
}
