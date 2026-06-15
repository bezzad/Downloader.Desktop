using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
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
    private const string OutDir = "/home/behzad-khosravifar/Documents/sources/Downloader.Desktop/docs/screenshots";

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
        config.Downloads.Add(Item("ubuntu-24.04.2-desktop.iso", 5_100_000_000, 3_162_000_000, DownloadStatus.Running));
        config.Downloads.Add(Item("interstellar-trailer.mp4", 240_000_000, 74_400_000, DownloadStatus.Running));
        config.Downloads.Add(Item("the-daily-podcast-ep12.mp3", 52_000_000, 18_200_000, DownloadStatus.Paused));
        config.Downloads.Add(Item("annual-report-2025.pdf", 12_400_000, 12_400_000, DownloadStatus.Completed));
        var failed = Item("project-photos.zip", 340_000_000, 41_000_000, DownloadStatus.Failed);
        failed.LastError = "Network error: the remote host could not be reached.";
        config.Downloads.Add(failed);
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

        // Loaded items are normalized to Paused; show the first two as actively downloading.
        foreach (var row in manager.Items.Take(2))
        {
            row.Status = DownloadStatus.Running;
            row.Speed = 8_400_000;
        }

        var window = new MainWindow { DataContext = vm };
        window.Show();

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        Save(window, "home-dark.png");

        // VERIFY: click a cell and confirm no per-cell focus/current border appears (#3/#8).
        Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, new Avalonia.Point(360, 240), Avalonia.Input.MouseButton.Left);
        Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, new Avalonia.Point(360, 240), Avalonia.Input.MouseButton.Left);
        Save(window, "verify-cellclick.png");

        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        Save(window, "home-light.png");

        // Settings page (dark)
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        vm.ShowSettingViewCommand.Execute(null);
        Save(window, "settings-dark.png");

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
