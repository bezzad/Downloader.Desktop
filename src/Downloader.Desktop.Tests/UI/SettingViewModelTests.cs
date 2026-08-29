using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The Settings page. Two kinds of property matter here and they fail differently:
///
/// 1. Setters that must "bite" — <see cref="SettingViewModel.MaxConcurrentDownloads"/> and
///    <see cref="SettingViewModel.MaxSpeedKbPerSecond"/> also have to reach the download manager.
///    MaxConcurrentDownloads historically only *seeded* new queues, so the user's "max 2" never
///    limited anything and ten downloads ran at once (SKILL.md). A regression there is invisible in
///    the UI: the number shows the right value while doing nothing.
/// 2. Plain pass-throughs onto <see cref="DownloadSettings"/>. Individually dull, but a setter wired
///    to the wrong backing field silently discards a user's choice, so they are checked by round-trip.
///
/// AvaloniaFact throughout: the view model reads Localizer and ThemeService, which need the headless
/// runtime (SKILL.md — a plain Fact gets raw keys back and is order-dependent).
/// </summary>
public class SettingViewModelTests
{
    private static (SettingViewModel vm, Config config, DownloadManager manager) Build()
    {
        Localizer.Instance.Load("en");
        var config = Config.New();
        var manager = new DownloadManager();
        manager.Initialize(config);
        return (new SettingViewModel(config, manager), config, manager);
    }

    // ---- the setters that must reach the manager ---------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Max_concurrent_downloads_writes_through_to_the_default_queue()
    {
        var (vm, config, _) = Build();

        vm.MaxConcurrentDownloads = 3;

        Assert.Equal(3, vm.MaxConcurrentDownloads);
        Assert.Equal(3, config.Settings.MaxConcurrentDownloads);

        // The cap the pump actually enforces lives on the queue. If these drift, the Settings number
        // is decorative and the limit never applies.
        Assert.Equal(3, config.DefaultQueue.MaxConcurrent);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Max_concurrent_downloads_is_clamped_to_at_least_one()
    {
        var (vm, config, _) = Build();

        vm.MaxConcurrentDownloads = 0;
        Assert.Equal(1, vm.MaxConcurrentDownloads);
        Assert.Equal(1, config.DefaultQueue.MaxConcurrent);

        vm.MaxConcurrentDownloads = -5;
        Assert.Equal(1, vm.MaxConcurrentDownloads);

        // A zero cap would stall the queue forever — nothing would ever start.
        Assert.True(config.DefaultQueue.MaxConcurrent >= 1);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Speed_limit_is_kilobytes_in_the_ui_and_bytes_in_the_engine_settings()
    {
        var (vm, config, _) = Build();

        vm.MaxSpeedKbPerSecond = 512;

        Assert.Equal(512, vm.MaxSpeedKbPerSecond);
        Assert.Equal(512L * 1024, config.Settings.MaximumBytesPerSecond);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Zero_or_negative_speed_limit_means_unlimited()
    {
        var (vm, config, _) = Build();

        vm.MaxSpeedKbPerSecond = 256;
        vm.MaxSpeedKbPerSecond = 0;

        Assert.Equal(0, vm.MaxSpeedKbPerSecond);
        Assert.Equal(0, config.Settings.MaximumBytesPerSecond);

        vm.MaxSpeedKbPerSecond = -1;
        Assert.Equal(0, vm.MaxSpeedKbPerSecond);
        Assert.Equal(0, config.Settings.MaximumBytesPerSecond);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Settings_work_without_a_manager()
    {
        // The page is constructible with no manager (design-time / early startup); the "bite" calls
        // are null-conditional and must not throw.
        Localizer.Instance.Load("en");
        var config = Config.New();
        var vm = new SettingViewModel(config);

        vm.MaxConcurrentDownloads = 4;
        vm.MaxSpeedKbPerSecond = 128;

        Assert.Equal(4, config.Settings.MaxConcurrentDownloads);
        Assert.Equal(128L * 1024, config.Settings.MaximumBytesPerSecond);
    }

    // ---- language, theme, accent ------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Selecting_a_language_persists_the_code_and_switches_the_localizer()
    {
        var (vm, config, _) = Build();
        try
        {
            var french = vm.Languages.First(l => l.Code == "fr");
            vm.SelectedLanguage = french;

            Assert.Equal("fr", config.Settings.Language);
            Assert.Equal("fr", vm.SelectedLanguage.Code);
            Assert.Equal("fr", Localizer.Instance.Current);
        }
        finally
        {
            Localizer.Instance.Load("en"); // don't leak a language into later tests
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Selecting_a_null_language_or_accent_is_ignored()
    {
        var (vm, config, _) = Build();
        var language = config.Settings.Language;
        var accent = config.Settings.AccentColor;

        vm.SelectedLanguage = null;
        vm.SelectedAccent = null;

        Assert.Equal(language, config.Settings.Language);
        Assert.Equal(accent, config.Settings.AccentColor);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_unknown_saved_language_falls_back_to_the_first_one()
    {
        Localizer.Instance.Load("en");
        var config = Config.New();
        config.Settings.Language = "kl"; // a language this build doesn't ship
        var vm = new SettingViewModel(config);

        Assert.NotNull(vm.SelectedLanguage);
        Assert.Equal(Localizer.Languages[0].Code, vm.SelectedLanguage.Code);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Selecting_an_accent_persists_the_key()
    {
        var (vm, config, _) = Build();

        vm.SelectedAccent = ThemeService.Accents.First(a => a.Key == "Amber");

        Assert.Equal("Amber", config.Settings.AccentColor);
        Assert.Equal("Amber", vm.SelectedAccent.Key);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Dark_theme_toggle_round_trips_through_the_config()
    {
        var (vm, config, _) = Build();

        vm.IsDarkTheme = true;
        Assert.True(vm.IsDarkTheme);
        Assert.Equal(ThemeVariant.Dark, config.ThemeMode);

        vm.IsDarkTheme = false;
        Assert.False(vm.IsDarkTheme);
        Assert.Equal(ThemeVariant.Light, config.ThemeMode);

        vm.SwitchThemeCommand.Execute(null); // applies the variant to the running app
    }

    // ---- update button -----------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Update_button_reads_check_for_updates_when_idle()
    {
        UpdateFlow.ResetForTests();
        var (vm, _, _) = Build();

        Assert.False(vm.IsUpdateDownloading);
        Assert.Equal(0, vm.UpdateProgress);
        Assert.Equal("0%", vm.UpdateProgressText);
        Assert.False(vm.HasAvailableVersion);
        Assert.Equal(string.Empty, vm.AvailableVersionText);

        Assert.False(string.IsNullOrWhiteSpace(vm.UpdateButtonText));
        Assert.DoesNotContain("Btn_", vm.UpdateButtonText); // localized, not a raw key
    }

    // ---- pass-through settings --------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Numeric_engine_settings_round_trip_onto_the_settings_model()
    {
        var (vm, config, _) = Build();
        var s = config.Settings;

        vm.ChunkCount = 6;
        vm.ParallelCount = 5;
        vm.BufferBlockSize = 4096;
        vm.MaxTryAgainOnFailure = 7;
        vm.BlockTimeout = 9000;
        vm.HttpClientTimeout = 120;
        vm.MinimumSizeOfChunking = 2048;
        vm.MinimumChunkSize = 1024;
        vm.MaxMemoryBufferMb = 512;
        vm.MaximumAutomaticRedirections = 12;
        vm.ConnectTimeout = 30;

        Assert.Equal(6, s.ChunkCount);
        Assert.Equal(5, s.ParallelCount);
        Assert.Equal(4096, s.BufferBlockSize);
        Assert.Equal(7, s.MaxTryAgainOnFailure);
        Assert.Equal(9000, s.BlockTimeout);
        Assert.Equal(120, s.HttpClientTimeout);
        Assert.Equal(2048, s.MinimumSizeOfChunking);
        Assert.Equal(1024, s.MinimumChunkSize);
        Assert.Equal(512, vm.MaxMemoryBufferMb);
        Assert.Equal(12, s.MaximumAutomaticRedirections);
        Assert.Equal(30, s.ConnectTimeout);

        // Read back through the view model too, so a getter bound to the wrong field is caught.
        Assert.Equal(6, vm.ChunkCount);
        Assert.Equal(5, vm.ParallelCount);
        Assert.Equal(4096, vm.BufferBlockSize);
        Assert.Equal(7, vm.MaxTryAgainOnFailure);
        Assert.Equal(9000, vm.BlockTimeout);
        Assert.Equal(120, vm.HttpClientTimeout);
        Assert.Equal(2048, vm.MinimumSizeOfChunking);
        Assert.Equal(1024, vm.MinimumChunkSize);
        Assert.Equal(12, vm.MaximumAutomaticRedirections);
        Assert.Equal(30, vm.ConnectTimeout);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Boolean_engine_settings_round_trip_onto_the_settings_model()
    {
        var (vm, config, _) = Build();
        var s = config.Settings;

        foreach (var value in new[] { true, false })
        {
            vm.ParallelDownload = value;
            vm.CheckDiskSizeBeforeDownload = value;
            vm.EnableAutoResumeDownload = value;
            vm.ClearPackageOnCompletionWithFailure = value;
            vm.AllowAutoRedirect = value;
            vm.KeepAlive = value;
            vm.RememberLastSavePath = value;

            Assert.Equal(value, s.ParallelDownload);
            Assert.Equal(value, s.CheckDiskSizeBeforeDownload);
            Assert.Equal(value, s.EnableAutoResumeDownload);
            Assert.Equal(value, s.ClearPackageOnCompletionWithFailure);
            Assert.Equal(value, s.AllowAutoRedirect);
            Assert.Equal(value, s.KeepAlive);
            Assert.Equal(value, s.RememberLastSavePath);

            Assert.Equal(value, vm.ParallelDownload);
            Assert.Equal(value, vm.CheckDiskSizeBeforeDownload);
            Assert.Equal(value, vm.EnableAutoResumeDownload);
            Assert.Equal(value, vm.ClearPackageOnCompletionWithFailure);
            Assert.Equal(value, vm.AllowAutoRedirect);
            Assert.Equal(value, vm.KeepAlive);
            Assert.Equal(value, vm.RememberLastSavePath);
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Request_string_settings_round_trip_onto_the_settings_model()
    {
        var (vm, config, _) = Build();
        var s = config.Settings;

        vm.DefaultSavePath = "/tmp/downloads";
        vm.DownloadFileExtension = ".part";
        vm.UserAgent = "Downloader/test";
        vm.Referer = "https://example.invalid/";
        vm.Accept = "*/*";
        vm.ProxyAddress = "http://127.0.0.1:8080";

        Assert.Equal("/tmp/downloads", s.DefaultSavePath);
        Assert.Equal(".part", s.DownloadFileExtension);
        Assert.Equal("Downloader/test", s.UserAgent);
        Assert.Equal("https://example.invalid/", s.Referer);
        Assert.Equal("*/*", s.Accept);
        Assert.Equal("http://127.0.0.1:8080", s.ProxyAddress);

        Assert.Equal("/tmp/downloads", vm.DefaultSavePath);
        Assert.Equal(".part", vm.DownloadFileExtension);
        Assert.Equal("Downloader/test", vm.UserAgent);
        Assert.Equal("https://example.invalid/", vm.Referer);
        Assert.Equal("*/*", vm.Accept);
        Assert.Equal("http://127.0.0.1:8080", vm.ProxyAddress);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Setting_a_property_notifies_so_the_debounced_save_fires()
    {
        var (vm, _, _) = Build();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.ChunkCount = 9;
        vm.UserAgent = "x";
        vm.IsDarkTheme = true;

        // MainViewModel hangs its SaveSoon() debounce off this event — a silent setter means the
        // change is never persisted.
        Assert.Contains(nameof(vm.ChunkCount), changed);
        Assert.Contains(nameof(vm.UserAgent), changed);
        Assert.Contains(nameof(vm.IsDarkTheme), changed);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_settings_page_exposes_its_commands_and_plugin_section()
    {
        var (vm, _, _) = Build();

        Assert.NotNull(vm.Plugins);
        Assert.NotNull(vm.SelectSavePathCommand);
        Assert.NotNull(vm.SwitchThemeCommand);
        Assert.NotNull(vm.OpenLogsFolderCommand);
        Assert.NotNull(vm.ExportLogsCommand);
        Assert.NotNull(vm.EmailLogsCommand);
        Assert.NotNull(vm.ResetDefaultsCommand);
        Assert.NotNull(vm.CheckUpdateCommand);
        Assert.NotNull(vm.CancelUpdateDownloadCommand);
        Assert.NotEmpty(vm.Languages);
        Assert.NotEmpty(vm.Accents);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_null_config_is_rejected_rather_than_failing_later()
    {
        Assert.Throws<ArgumentNullException>(() => new SettingViewModel(null));
    }

    // ---- app behaviour toggles --------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Toggling_notifications_drives_the_notification_service()
    {
        var wasEnabled = NotificationService.Enabled;
        try
        {
            var (vm, config, _) = Build();
            vm.EnableNotifications = false;

            Assert.False(config.Settings.EnableNotifications);
            Assert.False(NotificationService.Enabled);

            // Turning it back on would fire a sample notification, which shells out to notify-send on
            // this box; the off direction is the one that must be honoured, and it is asserted above.
            Assert.False(vm.EnableNotifications);
        }
        finally
        {
            NotificationService.Enabled = wasEnabled;
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_per_event_notification_toggles_round_trip()
    {
        var (vm, config, _) = Build();

        foreach (var value in new[] { false, true })
        {
            vm.NotifyOnComplete = value;
            vm.NotifyOnFailed = value;
            vm.NotifyOnAllComplete = value;
            vm.NotifyOnShutdown = value;

            Assert.Equal(value, config.Settings.NotifyOnComplete);
            Assert.Equal(value, config.Settings.NotifyOnFailed);
            Assert.Equal(value, config.Settings.NotifyOnAllComplete);
            Assert.Equal(value, config.Settings.NotifyOnShutdown);

            Assert.Equal(value, vm.NotifyOnComplete);
            Assert.Equal(value, vm.NotifyOnFailed);
            Assert.Equal(value, vm.NotifyOnAllComplete);
            Assert.Equal(value, vm.NotifyOnShutdown);
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Shutdown_on_completion_round_trips()
    {
        var (vm, config, _) = Build();

        vm.ShutdownOnCompletion = true;
        Assert.True(config.Settings.ShutdownOnCompletion);

        vm.ShutdownOnCompletion = false;
        Assert.False(config.Settings.ShutdownOnCompletion);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Toggling_logging_drives_the_app_log()
    {
        var (vm, config, _) = Build();
        var was = config.Settings.EnableLogging;
        try
        {
            vm.EnableLogging = true;
            Assert.True(config.Settings.EnableLogging);

            vm.EnableLogging = false;
            Assert.False(config.Settings.EnableLogging);
        }
        finally
        {
            vm.EnableLogging = was;
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_local_api_row_reports_an_address_and_a_status()
    {
        var (vm, _, _) = Build();

        // Always shows an address — the preferred port when nothing is bound yet — so the row never
        // renders blank while the listener is still starting.
        Assert.StartsWith("127.0.0.1:", vm.LocalApiAddress);
        Assert.NotNull(vm.LocalApiStatusBrush);
        Assert.False(string.IsNullOrWhiteSpace(vm.LocalApiStatusText));
        Assert.DoesNotContain("Set_LocalApi", vm.LocalApiStatusText); // localized
        Assert.Equal(LocalApiService.IsRunning, vm.IsLocalApiRunning);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_notch_overlay_toggle_starts_and_stops_the_overlay()
    {
        var (vm, config, _) = Build();
        try
        {
            vm.EnableNotch = true;
            Assert.True(config.Settings.EnableNotch);
            Assert.True(vm.EnableNotch);

            vm.EnableNotch = false;
            Assert.False(config.Settings.EnableNotch);
            Assert.False(NotchService.IsActive);

            // Setting the same value again is a no-op (it must not restart the overlay).
            vm.EnableNotch = false;
            Assert.False(NotchService.IsActive);
        }
        finally
        {
            NotchService.Stop();
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_browser_integration_toggle_starts_and_stops_the_local_api()
    {
        var (vm, config, _) = Build();
        try
        {
            vm.EnableBrowserIntegration = true;
            Assert.True(config.Settings.EnableBrowserIntegration);
            // Whether the listener actually binds depends on the machine's free ports, but the row
            // must always describe the current state rather than going stale.
            Assert.StartsWith("127.0.0.1:", vm.LocalApiAddress);
            Assert.Equal(LocalApiService.IsRunning, vm.IsLocalApiRunning);

            vm.EnableBrowserIntegration = false;
            Assert.False(config.Settings.EnableBrowserIntegration);
            Assert.False(LocalApiService.IsRunning);
            Assert.False(vm.IsLocalApiRunning);

            vm.EnableBrowserIntegration = false; // no-op
            Assert.False(LocalApiService.IsRunning);
        }
        finally
        {
            // The listener is a process-wide singleton: leaving it bound would make another test's
            // "the preferred port is taken" scenario read as "already running" and no-op.
            LocalApiService.Stop();
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_tray_toggle_is_safe_to_flip_while_run_at_startup_is_off()
    {
        var (vm, config, _) = Build();

        // Guard: turning the tray OFF also turns run-at-startup off, which writes to the developer's
        // real autostart entry. Keeping the setting false means that branch is not taken here — the
        // coupling itself is asserted in the next test without touching the machine.
        config.Settings.RunAtStartup = false;

        vm.EnableSystemTray = true;
        Assert.True(config.Settings.EnableSystemTray);
        Assert.True(vm.EnableSystemTray);

        vm.EnableSystemTray = false;
        Assert.False(config.Settings.EnableSystemTray);
        Assert.False(vm.EnableSystemTray);

        // Setting the same value twice must not re-run the tray plumbing.
        vm.EnableSystemTray = false;
        Assert.False(vm.EnableSystemTray);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Run_at_startup_is_recorded_as_off_when_the_tray_is_turned_off()
    {
        var (vm, config, _) = Build();
        var startupWasEnabled = StartupService.IsEnabled();
        try
        {
            config.Settings.EnableSystemTray = true;
            config.Settings.RunAtStartup = true;

            vm.EnableSystemTray = false;

            // Launching at login only makes sense with the tray: the app starts minimized into it.
            // Leaving RunAtStartup on with no tray would start a hidden app with no way to reach it.
            Assert.False(config.Settings.RunAtStartup);
            Assert.False(vm.RunAtStartup);
        }
        finally
        {
            // That path calls StartupService.Apply(false); put the real machine state back.
            StartupService.Apply(startupWasEnabled);
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_remaining_simple_settings_round_trip()
    {
        var (vm, config, _) = Build();

        vm.AutoUpdate = false;
        Assert.False(config.Settings.AutoUpdate);
        vm.AutoUpdate = true;
        Assert.True(config.Settings.AutoUpdate);

        vm.SelectedFileExistPolicy = FileExistPolicy.Delete;
        Assert.Equal(FileExistPolicy.Delete, config.Settings.FileExistPolicy);
        vm.SelectedFileExistPolicy = FileExistPolicy.IgnoreDownload;
        Assert.Equal(FileExistPolicy.IgnoreDownload, vm.SelectedFileExistPolicy);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Reset_to_defaults_restores_every_setting_and_the_theme()
    {
        var (vm, config, _) = Build();

        // Guard the developer's real machine state: ResetDefaults re-applies run-at-startup, which
        // writes (or deletes) a real autostart entry. Put back whatever was there.
        var startupWasEnabled = StartupService.IsEnabled();
        var apiWasRunning = LocalApiService.IsRunning;
        var notificationsWereEnabled = NotificationService.Enabled;
        try
        {
            vm.ChunkCount = 15;
            vm.UserAgent = "changed";
            vm.MaxConcurrentDownloads = 9;
            vm.IsDarkTheme = true;

            vm.ResetDefaultsCommand.Execute(null);

            var defaults = DownloadSettings.New();
            Assert.Equal(defaults.ChunkCount, config.Settings.ChunkCount);
            Assert.Equal(defaults.UserAgent, config.Settings.UserAgent);
            Assert.Equal(defaults.MaxConcurrentDownloads, config.Settings.MaxConcurrentDownloads);
            Assert.Equal(ThemeVariant.Light, config.ThemeMode);
        }
        finally
        {
            StartupService.Apply(startupWasEnabled);
            if (!apiWasRunning)
                LocalApiService.Stop();
            NotificationService.Enabled = notificationsWereEnabled;
            NotchService.Stop();
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_about_card_version_matches_the_update_check()
    {
        var (vm, _, _) = Build();

        // If these ever drift, every patch release looks "newer forever" (#update-false-alarm).
        Assert.Equal(UpdateService.CurrentVersion.ToString(), vm.AppVersion);
    }

    /// <summary>
    /// One button carries the whole update flow — "Check for updates" becomes "Download update" and
    /// then "Restart to update". The label is driven off a static coordinator via an event, so if the
    /// page fails to re-read it the user is left pressing a button that still says "Check for updates"
    /// while an update sits there waiting.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async System.Threading.Tasks.Task The_update_button_follows_the_flow_it_is_driving()
    {
        var notificationsWereEnabled = NotificationService.Enabled;
        NotificationService.Enabled = false;
        UpdateFlow.ResetForTests();
        try
        {
            var (vm, _, _) = Build();
            var changed = new List<string>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");

            Assert.Equal(Localizer.Instance["Btn_CheckUpdate"], vm.UpdateButtonText);
            Assert.False(vm.HasAvailableVersion);
            Assert.Empty(vm.AvailableVersionText);
            Assert.False(vm.IsUpdateDownloading);
            Assert.Equal("0%", vm.UpdateProgressText);

            UpdateFlow.PromptUpdate = _ => { };
            UpdateFlow.CheckOverride = () => System.Threading.Tasks.Task.FromResult(new UpdateInfo
            {
                Version = "99.0.0",
                Tag = "v99.0.0",
                AssetUrl = "https://host/Downloader.tar.gz",
                AssetName = "Downloader.tar.gz",
                ReleaseUrl = "https://host/releases/v99.0.0"
            });

            await UpdateFlow.CheckAsync(manual: true);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal(Localizer.Instance["Update_DownloadBtn"], vm.UpdateButtonText);
            Assert.True(vm.HasAvailableVersion);
            Assert.Contains("v99.0.0", vm.AvailableVersionText);
            Assert.Contains(nameof(vm.UpdateButtonText), changed);
            Assert.Contains(nameof(vm.AvailableVersionText), changed);
        }
        finally
        {
            UpdateFlow.ResetForTests();
            NotificationService.Enabled = notificationsWereEnabled;
        }
    }

    /// <summary>
    /// Turning notifications on fires a sample immediately — without it the user has no way to tell
    /// whether their desktop actually delivers them, which is the whole reason the sample exists.
    /// Turning it back on when it was already on must NOT fire a second one.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Turning_notifications_on_sends_one_sample_so_the_user_can_see_it_work()
    {
        var notificationsWereEnabled = NotificationService.Enabled;
        var sent = new List<string[]>();
        ShellLauncher.RunOverride = (_, args) => { sent.Add(args); return true; };
        try
        {
            var (vm, config, _) = Build();
            vm.EnableNotifications = false;
            sent.Clear();

            vm.EnableNotifications = true;

            Assert.True(config.Settings.EnableNotifications);
            Assert.True(NotificationService.Enabled);
            // Only Linux posts its notification through a launched command (notify-send); macOS and
            // Windows post in-process, so there the sample is not observable through ShellLauncher.
            var expected = OperatingSystem.IsLinux() ? 1 : 0;
            Assert.Equal(expected, sent.Count);
            if (expected == 1)
                Assert.Contains(sent[0], a => a.Contains("Notifications enabled"));

            // Already on — no second sample.
            vm.EnableNotifications = true;
            Assert.Equal(expected, sent.Count);

            vm.EnableNotifications = false;
            Assert.False(NotificationService.Enabled);
            Assert.Equal(expected, sent.Count);
        }
        finally
        {
            ShellLauncher.RunOverride = null;
            NotificationService.Enabled = notificationsWereEnabled;
        }
    }
}
