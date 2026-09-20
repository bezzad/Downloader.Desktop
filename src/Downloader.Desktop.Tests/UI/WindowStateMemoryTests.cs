using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using ReactiveUI;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The main window remembering how it was left (issue #15), end to end through the shell: the saved
/// layout is applied when the app starts, and re-arranging the window updates what will be saved.
///
/// The geometry decisions themselves live in <see cref="WindowLayoutPolicy"/> and are covered by
/// <c>Unit/WindowLayoutPolicyTests</c>; what is checked here is the wiring that used to be missing
/// entirely — nothing read or wrote the window's state at all.
/// </summary>
public class WindowStateMemoryTests : IDisposable
{
    private readonly IScheduler _realScheduler = RxApp.MainThreadScheduler;
    private readonly bool _notificationsWereEnabled = NotificationService.Enabled;

    public WindowStateMemoryTests()
    {
        // Without this the shell's init runs inline, before View is assigned, and SetupAppShell —
        // where the layout is restored and tracked — is silently skipped. See DeferringScheduler.
        RxApp.MainThreadScheduler = new DeferringScheduler();
        NotificationService.Enabled = false;
        StartupService.ApplyOverride = _ => { };
        Localizer.Instance.Load("en");
    }

    public void Dispose()
    {
        RxApp.MainThreadScheduler = _realScheduler;
        StartupService.ApplyOverride = null;
        NotificationService.Enabled = _notificationsWereEnabled;
        LocalApiService.Stop();
    }

    private sealed class StubFileService(Config config) : IFileService
    {
        public Task<Config> LoadFromFileAsync() => Task.FromResult(config);
        public Task SaveToFileAsync(Config itemToSave) => Task.CompletedTask;
    }

    private static Config QuietConfig()
    {
        var config = Config.New();
        config.DisabledPlugins ??= new List<string>();
        var s = config.Settings;
        s.EnableSystemTray = false;
        s.EnableNotch = false;
        s.EnableNotifications = false;
        s.RunAtStartup = false;
        s.AutoUpdate = false;
        s.EnableBrowserIntegration = false;
        return config;
    }

    private static (MainViewModel Main, Window Window) Start(Config config)
    {
        LocalApiService.Stop();

        var main = new MainViewModel(new StubFileService(config), new DownloadManager(), new PluginManager());
        var window = new Window { MinWidth = 840, MinHeight = 500, Width = 1000, Height = 620 };
        // The window must really be shown: a resize is observed through ClientSize (Avalonia does not
        // write a user resize back into Width/Height), and an unshown window never lays out.
        window.Show();
        main.View = window;

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (main.Downloads == null)
        {
            Dispatcher.UIThread.RunJobs();
            if (DateTime.UtcNow > deadline)
                Assert.Fail("the shell never finished initialising");
            Thread.Sleep(5);
        }
        Dispatcher.UIThread.RunJobs();
        return (main, window);
    }

    /// <summary>Pumps until <paramref name="until"/> holds, so a posted layout event is not missed.</summary>
    private static void Pump(Func<bool> until, int milliseconds = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (!until() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void A_remembered_size_is_applied_at_startup()
    {
        var config = QuietConfig();
        config.MainWindow = new WindowLayout { Width = 1180, Height = 660, X = 140, Y = 90 };

        var (_, window) = Start(config);
        try
        {
            Assert.Equal(1180, window.Width);
            Assert.Equal(660, window.Height);
            Assert.NotEqual(WindowState.Maximized, window.WindowState);

            // A position is only applied when the platform can say which screens exist; headless
            // backends do not always report any, and guessing would be worse than not placing it.
            if (window.Screens?.All?.Count > 0)
                Assert.Equal(new PixelPoint(140, 90), window.Position);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void A_window_left_maximized_reopens_maximized()
    {
        var config = QuietConfig();
        config.MainWindow = new WindowLayout { IsMaximized = true, Width = 1180, Height = 660, X = 140, Y = 90 };

        var (_, window) = Start(config);
        try
        {
            // The headline of issue #15.
            Assert.Equal(WindowState.Maximized, window.WindowState);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void A_first_run_leaves_the_window_at_its_declared_defaults()
    {
        var config = QuietConfig();   // nothing remembered

        var (_, window) = Start(config);
        try
        {
            Assert.Equal(1000, window.Width);
            Assert.Equal(620, window.Height);
            Assert.NotEqual(WindowState.Maximized, window.WindowState);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void Resizing_the_window_is_recorded_without_waiting_for_an_exit()
    {
        var config = QuietConfig();

        var (_, window) = Start(config);
        try
        {
            window.Width = 1240;
            window.Height = 700;
            Pump(() => config.MainWindow != null && Math.Abs(config.MainWindow.Width - 1240) < 1);

            Assert.NotNull(config.MainWindow);
            Assert.Equal(1240, config.MainWindow.Width, 0);
            Assert.Equal(700, config.MainWindow.Height, 0);
            Assert.False(config.MainWindow.IsMaximized);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void Maximizing_is_recorded_but_does_not_lose_the_normal_size()
    {
        var config = QuietConfig();

        var (_, window) = Start(config);
        try
        {
            window.Width = 1240;
            window.Height = 700;
            Pump(() => config.MainWindow != null && Math.Abs(config.MainWindow.Width - 1240) < 1);

            window.WindowState = WindowState.Maximized;
            Pump(() => config.MainWindow?.IsMaximized == true);

            Assert.True(config.MainWindow.IsMaximized);
            // If the maximized frame overwrote these, "restore down" after a restart would land
            // somewhere the user never chose.
            Assert.Equal(1240, config.MainWindow.Width, 0);
            Assert.Equal(700, config.MainWindow.Height, 0);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void Minimizing_does_not_overwrite_what_is_remembered()
    {
        var config = QuietConfig();

        var (_, window) = Start(config);
        try
        {
            window.Width = 1240;
            window.Height = 700;
            Pump(() => config.MainWindow != null && Math.Abs(config.MainWindow.Width - 1240) < 1);

            window.WindowState = WindowState.Minimized;
            Pump(() => false, 200);   // give any stray event a chance to land

            Assert.False(config.MainWindow.IsMaximized);
            Assert.Equal(1240, config.MainWindow.Width, 0);
            Assert.Equal(700, config.MainWindow.Height, 0);
        }
        finally
        {
            window.WindowState = WindowState.Normal;
            window.Close();
        }
    }
}
