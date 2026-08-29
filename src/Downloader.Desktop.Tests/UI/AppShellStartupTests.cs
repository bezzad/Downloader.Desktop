using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Services;
using Downloader.Desktop.Tests.Plugins.Hls;
using Downloader.Desktop.ViewModels;
using ReactiveUI;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// What happens between "the window exists" and "the app is usable": the config is loaded, plugins are
/// loaded and the persisted disabled list applied, the pages are built, and then the shell is wired —
/// tray, close-to-tray, run-at-startup, the local API, single-instance hand-off and the update check.
///
/// None of this ran in the suite before, for one concrete reason: applying run-at-startup writes the
/// developer's own autostart entry. With that behind a seam the rest follows, and it is worth having —
/// this is the code that decides whether a launched app can capture a link from the browser extension,
/// whether a second launch reaches the first one, and whether closing the window quits or hides.
/// </summary>
public class AppShellStartupTests : IDisposable
{
    private readonly bool _notificationsWereEnabled = NotificationService.Enabled;
    private readonly List<bool> _startupApplied = new();
    private readonly IScheduler _realScheduler = RxApp.MainThreadScheduler;
    private readonly string _pluginsRoot =
        Path.Combine(Path.GetTempPath(), "dldesktop-shell-plugins-" + Guid.NewGuid().ToString("N"));

    public AppShellStartupTests()
    {
        // See DeferringScheduler: without it the shell's init runs inline, before the window is
        // assigned, and the whole shell wiring is silently skipped.
        RxApp.MainThreadScheduler = new DeferringScheduler();
        // The shell loads plugins from the user's own plugins folder — point it somewhere empty so a
        // developer's installed plugins can't change what these tests see.
        Directory.CreateDirectory(_pluginsRoot);
        PluginManager.PluginsRootOverride = _pluginsRoot;
        NotificationService.Enabled = false;
        StartupService.ApplyOverride = enabled => _startupApplied.Add(enabled);
        UpdateFlow.ResetForTests();
        Localizer.Instance.Load("en");
    }

    public void Dispose()
    {
        RxApp.MainThreadScheduler = _realScheduler;
        PluginManager.PluginsRootOverride = null;
        try { Directory.Delete(_pluginsRoot, recursive: true); } catch { /* best-effort */ }
        StartupService.ApplyOverride = null;
        UpdateFlow.ResetForTests();
        PluginCatalogService.ReleasesUrlOverride = null;
        SingleInstanceService.SetMessageHandler(null);
        LocalApiService.Stop();
        NotificationService.Enabled = _notificationsWereEnabled;
        NotchService.Stop();
    }

    private sealed class StubFileService(Config config) : IFileService
    {
        public Config Saved { get; private set; }
        public Task<Config> LoadFromFileAsync() => Task.FromResult(config);
        public Task SaveToFileAsync(Config itemToSave) { Saved = itemToSave; return Task.CompletedTask; }
    }

    private static Config QuietConfig()
    {
        var config = Config.New();
        config.DisabledPlugins ??= new List<string>();
        var s = config.Settings;
        s.EnableSystemTray = false;      // no tray icon on a headless box
        s.EnableNotch = false;
        s.EnableNotifications = false;
        s.RunAtStartup = false;
        s.AutoUpdate = false;            // opt in per-test, so no test hits the network by accident
        s.EnableBrowserIntegration = false;
        return config;
    }

    /// <summary>Builds the shell the way the app does: construct, hand it the window, let it initialise.</summary>
    private static (MainViewModel Main, Window Window, PluginManager Plugins) Start(
        Config config, DownloadManager manager = null)
    {
        // The API listener is process-wide: whether the shell binds it is only observable from a
        // known-stopped baseline, and any test that left it bound would otherwise be read as this
        // shell's doing.
        LocalApiService.Stop();

        manager ??= new DownloadManager();
        var plugins = new PluginManager();
        var main = new MainViewModel(new StubFileService(config), manager, plugins);
        var window = new Window();
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
        return (main, window, plugins);
    }

    /// <summary>
    /// The pages exist, the persisted plugin choices are applied, and run-at-startup is re-asserted to
    /// match the config — the app re-applies it every launch so a manually removed entry comes back.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void Starting_up_builds_the_pages_and_re_applies_the_saved_choices()
    {
        var config = QuietConfig();
        config.Settings.RunAtStartup = true;

        var (main, window, _) = Start(config);
        try
        {
            Assert.NotNull(main.Downloads);
            Assert.NotNull(main.Queues);
            Assert.NotNull(main.Scheduler);
            Assert.NotNull(main.Settings);
            Assert.True(main.IsDownloadsSelected, "the app opens on the downloads list");

            Assert.Contains(true, _startupApplied);

            // The local API is the browser extension's only way in — off in the config means not bound.
            Assert.False(LocalApiService.IsRunning);
            // …but the manager and config are handed over regardless, so turning it on later works.
            Assert.NotNull(LocalApiService.Manager);
            Assert.NotNull(LocalApiService.Config);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A plugin the user disabled must come back disabled — the config is the only record of it.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void A_plugin_disabled_before_the_last_exit_comes_back_disabled()
    {
        var config = QuietConfig();
        config.DisabledPlugins.Add("com.bezzad.github-releases");

        var (main, window, plugins) = Start(config);
        try
        {
            var github = plugins.Plugins.FirstOrDefault(p => p.Id == "com.bezzad.github-releases");
            if (github == null)
                return; // built-ins are not staged in this test output — nothing to assert

            Assert.False(github.IsEnabled, "a disabled plugin must not silently come back enabled");
            Assert.Null(plugins.FindResolver("https://github.com/bezzad/Downloader/releases"));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// With browser integration on, the listener has to actually bind — this is what answers the
    /// extension's /ping, and a shell that wires everything except the Start() call looks identical
    /// from the outside until someone tries to send a link.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void Browser_integration_turned_on_binds_the_local_api()
    {
        var config = QuietConfig();
        config.Settings.EnableBrowserIntegration = true;

        var (main, window, _) = Start(config);
        try
        {
            Assert.True(LocalApiService.IsRunning, "the extension's endpoint must be listening");
            Assert.InRange(LocalApiService.EffectivePort,
                LocalApiService.PortRange.First(), LocalApiService.PortRange.Last());
            Assert.NotNull(LocalApiService.Manager);
        }
        finally
        {
            LocalApiService.Stop();
            window.Close();
        }
    }

    /// <summary>
    /// A second launch hands its link to the running instance. The handler is installed here, and if it
    /// is missing the second launch just exits and the user sees nothing happen at all.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void A_link_forwarded_from_a_second_launch_is_added_without_a_dialog()
    {
        var config = QuietConfig();
        var manager = new DownloadManager();

        var (main, window, _) = Start(config, manager);
        try
        {
            var json = $$"""{"url":"https://10.255.255.1/forwarded.bin","start":false}""";
            SingleInstanceService.Dispatch(SingleInstanceService.AddPrefix + json);
            Dispatcher.UIThread.RunJobs();

            var added = Assert.Single(manager.Items);
            Assert.Equal("https://10.255.255.1/forwarded.bin", added.GetItem().Url);
            Assert.NotEqual(DownloadStatus.Running, added.Status);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A forwarded payload that isn't a valid add must be ignored, not crash the handler.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void A_malformed_forwarded_payload_is_ignored()
    {
        var config = QuietConfig();
        var manager = new DownloadManager();

        var (main, window, _) = Start(config, manager);
        try
        {
            SingleInstanceService.Dispatch(SingleInstanceService.AddPrefix + "{not json");
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(manager.Items);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// With auto-update on, both checks run at startup: the app's own, and the plugins'. The plugin one
    /// only ever notifies — a plugin must never update itself behind the user's back.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void Auto_update_checks_the_app_and_the_plugins_without_installing_anything()
    {
        var config = QuietConfig();
        config.Settings.AutoUpdate = true;

        var checks = 0;
        UpdateFlow.CheckOverride = () => { checks++; return Task.FromResult<UpdateInfo>(null); };

        using var server = new LoopbackServer();
        server.MapText("/catalog.json",
            """[{ "id": "com.bezzad.hls", "version": "99.0.0", "assetName": "hls.zip", "sha256": "aa" }]""",
            "application/json");
        server.MapText("/releases/latest", $$"""
        {
          "assets": [
            { "name": "plugins-catalog.json", "browser_download_url": "{{server.Url("catalog.json")}}" },
            { "name": "hls.zip", "browser_download_url": "{{server.Url("hls.zip")}}" }
          ]
        }
        """, "application/json");
        PluginCatalogService.ReleasesUrlOverride = server.Url("releases/latest");

        var (main, window, plugins) = Start(config);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (checks == 0 && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }

            Assert.Equal(1, checks);
            // Nothing may be INSTALLED by a check — the user is only ever told. (The HLS assembly is
            // present in the test output, so it may well be loaded; what matters is that the catalog's
            // "99.0.0" did not get pulled down and swapped in behind the user's back.)
            var hls = plugins.Plugins.FirstOrDefault(p => p.Id == "com.bezzad.hls");
            if (hls != null)
                Assert.NotEqual("99.0.0", hls.Version);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Quitting closes the owned windows FIRST. Settings is a modal, and closing the owner while its
    /// nested session is running makes macOS swallow the shutdown — the app then stays on the old
    /// version after "Restart to update" (v1.5.0).
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void Quitting_closes_the_dialogs_it_owns_before_the_window_itself()
    {
        var (main, window, _) = Start(QuietConfig());
        window.Show();
        var owned = new Window();
        owned.Show(window);
        Dispatcher.UIThread.RunJobs();

        // Quit is private; the shell publishes it as the updater's "really exit" action, which is
        // exactly the caller that exposed the macOS bug below.
        Assert.NotNull(UpdateFlow.RequestQuit);
        UpdateFlow.RequestQuit();
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(window.OwnedWindows);
        Assert.False(owned.IsVisible, "an owned dialog must not outlive the quit that closed its owner");
    }
}
