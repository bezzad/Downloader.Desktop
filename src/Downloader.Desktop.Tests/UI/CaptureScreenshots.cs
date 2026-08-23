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
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>A no-op plugin used only to populate the Plugins-page screenshot.</summary>
internal sealed class DemoPlugin(string name, string description) : IDownloaderPlugin
{
    public string Id => "demo." + name.GetHashCode();
    public string Name => name;
    public string Version => "1.0.0";
    public string Author => "bezzad";
    public string Description => description;
    public void Initialize(IPluginContext context) { }
}

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

        // A second queue so the per-row Queue column renders (it only shows with 2+ queues).
        var media = new DownloadQueue { Name = "Media", MaxConcurrent = 2 };
        config.Queues.Add(media);

        config.Downloads.Add(Item("ubuntu-24.04.2-desktop.iso", 5_100_000_000, 3_162_000_000, DownloadStatus.Running));
        config.Downloads.Add(Item("interstellar-trailer.mp4", 240_000_000, 74_400_000, DownloadStatus.Running, media.Id));
        config.Downloads.Add(Item("the-daily-podcast-ep12.mp3", 52_000_000, 18_200_000, DownloadStatus.Paused, media.Id));
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

    private static DownloadItem Item(string name, long size, long got, DownloadStatus status, string queueId = null) => new()
    {
        Url = "https://example.com/files/" + name,
        SaveFolder = "/home/user/Downloads",
        FileName = name,
        Size = size,
        Downloaded = got,
        Status = status,
        QueueId = queueId,
        LastTry = DateTime.Now
    };

    private static void Pump(int times = 8)
    {
        for (var i = 0; i < times; i++)
            Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Scrolls a ScrollViewer vertically and lets layout settle before the next capture.</summary>
    private static void ScrollTo(Avalonia.Controls.ScrollViewer sv, double y)
    {
        sv.Offset = new Avalonia.Vector(0, Math.Max(0, y));
        Pump();
    }

    private static void Save(Avalonia.Controls.Window window, string file)
    {
        Pump();
        var frame = window.CaptureRenderedFrame();
        Directory.CreateDirectory(OutDir);
        frame!.Save(Path.Combine(OutDir, file));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Capture()
    {
        if (Environment.GetEnvironmentVariable("DLDESKTOP_CAPTURE") != "1")
            return;

        var manager = new DownloadManager();
        var plugins = new Services.PluginManager();
        plugins.RegisterPlugin(new DemoPlugin("HLS downloader", "Download HLS (.m3u8) streams and pick a quality."));
        plugins.RegisterPlugin(new DemoPlugin("Torrents", "Add magnet links and .torrent files."));
        var vm = new MainViewModel(new SampleFileService(SampleConfig()), manager, plugins);
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

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        // Management pages open IN the main window now (toolbar nav swaps the central content).
        vm.ShowSettingViewCommand.Execute(null);
        Save(window, "settings-dark.png");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        Save(window, "settings-light.png");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        // Scroll to the Theme/Accent/Language(flag) controls so the accent picker + flag are visible.
        Pump();
        // Pick the PAGE scroller, not just the first ScrollViewer in the tree: every ComboBox carries an
        // internal PART_ScrollViewer, and one of those came first, so the Offset below was being applied
        // to an unscrollable 150x20 popup and silently clamped to 0 — the settings-accent-* shots were
        // byte-identical duplicates of the unscrolled settings-* shots. Match on "actually scrollable".
        var sv = window.GetVisualDescendants().OfType<Downloader.Desktop.Views.SettingView>().FirstOrDefault()
            ?.GetVisualDescendants().OfType<Avalonia.Controls.ScrollViewer>()
            .OrderByDescending(s => s.Extent.Height - s.Viewport.Height)
            .FirstOrDefault(s => s.Extent.Height > s.Viewport.Height);
        if (sv != null)
        {
            ScrollTo(sv, 215);
            Save(window, "settings-accent-dark.png");
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            Save(window, "settings-accent-light.png");
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

            // Logging section: its three action buttons sit well below the fold, and they were the
            // control that read as invisible in dark mode (Fluent's default button background over a
            // dark card). Capture both themes so that contrast stays reviewable. Locate the buttons
            // rather than hard-coding an offset, so the shot keeps framing them as Settings grows.
            var logButton = window.GetVisualDescendants().OfType<Avalonia.Controls.Button>()
                .FirstOrDefault(b => b.Classes.Contains("secondary"));
            if (logButton != null)
            {
                // Position relative to the viewport + the offset already applied = position in content.
                // Back off ~180px so the section header and its hint text are in frame too.
                var y = logButton.TranslatePoint(new Avalonia.Point(0, 0), sv)?.Y ?? 0;
                ScrollTo(sv, sv.Offset.Y + y - 180);
                Save(window, "settings-logging-dark.png");
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                Save(window, "settings-logging-light.png");
                Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            }
            ScrollTo(sv, 0);
        }

        vm.ShowQueuesCommand.Execute(null);
        Save(window, "queues-dark.png");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        Save(window, "queues-light.png");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        vm.ShowSchedulerCommand.Execute(null);
        Save(window, "scheduler-dark.png");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        Save(window, "scheduler-light.png");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        vm.ShowDownloadsCommand.Execute(null);

        // Persian (RTL) home to verify translation + right-to-left mirroring.
        Localizer.Instance.Load("fa");
        Save(window, "home-fa-dark.png");
        Localizer.Instance.Load("en");

        // Details window (dark) — needs a live Configuration so the speed-limit numeric shows.
        var detItem = manager.Items.First();
        detItem.Configuration = new DownloadConfiguration { MaximumBytesPerSecond = 512 * 1024 };
        var det = new DownloadDetailsView { DataContext = new DownloadDetailsViewModel(detItem) };
        det.Show();
        Save(det, "details-dark.png");

        // Details for a STOPPED row (#6): only a row that is not running shows the "Refresh link" panel,
        // where a fresh link replaces an expired one and the download continues from the partial file.
        var stoppedItem = manager.Items.FirstOrDefault(i => i.Status is not DownloadStatus.Running)
                          ?? manager.Items.First();
        stoppedItem.Status = DownloadStatus.Failed;
        stoppedItem.ErrorMessage = Localizer.Instance["Error_LinkExpiredRefresh"];
        var detStopped = new DownloadDetailsView { DataContext = new DownloadDetailsViewModel(stoppedItem) };
        detStopped.Show();
        Save(detStopped, "details-refresh-dark.png");

        // In-app "update available" dialog (Download / Later) — the new user-initiated update prompt.
        var upd = new UpdatePromptView
        {
            DataContext = new UpdatePromptViewModel("1.4.0", "https://github.com/bezzad/Downloader.Desktop/releases")
        };
        upd.Show();
        Save(upd, "update-dialog-dark.png");

        // About dialog — verifies the distinct modal chrome (#17: accent border vs the main window).
        var about = new AboutView { DataContext = new AboutViewModel() };
        about.Show();
        Save(about, "about-dark.png");

        // In-app Donate modal (replaces the browser round-trip to Donate.md).
        var donate = new DonateView { DataContext = new DonateViewModel() };
        donate.Show();
        Save(donate, "donate-dark.png");
    }
}
