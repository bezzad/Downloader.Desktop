using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The small services that sit between the app and the outside world: notifications, the file logger,
/// URL/name resolution, the notch overlay, and the SDK's default interface implementations.
///
/// Most of these are "must never throw" code — a logger that throws takes down whatever it was
/// logging, a notification that throws kills the download completion path that raised it. So the
/// failure cases get as much attention as the happy ones.
/// </summary>
public class SmallServiceTests : IDisposable
{
    private readonly List<(string File, string[] Args)> _ran = new();
    private readonly bool _notificationsWereEnabled = NotificationService.Enabled;
    private readonly bool _loggingWasEnabled = AppLog.IsEnabled;

    public SmallServiceTests()
    {
        ShellLauncher.RunOverride = (file, args) => { _ran.Add((file, args)); return true; };
        ShellLauncher.OpenOverride = _ => true;
    }

    public void Dispose()
    {
        ShellLauncher.RunOverride = null;
        ShellLauncher.OpenOverride = null;
        NotificationService.Enabled = _notificationsWereEnabled;
        AppLog.SetEnabled(_loggingWasEnabled);
    }

    // ---- notifications -----------------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_master_switch_silences_passive_alerts()
    {
        NotificationService.Enabled = false;

        NotificationService.Notify("t", "m", isError: false);
        NotificationService.NotifyCompleted("file.zip");
        NotificationService.NotifyFailed("file.zip", "boom");
        NotificationService.NotifyAllCompleted(3);

        Assert.Empty(_ran);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Direct_feedback_is_shown_even_with_notifications_off()
    {
        NotificationService.Enabled = false;

        // Inform() answers something the user just did (e.g. "Plugin installed"), so it deliberately
        // ignores the passive-alert switch.
        NotificationService.Inform("Plugin installed", "HLS", isError: false);

        if (OperatingSystem.IsLinux())
            Assert.Single(_ran);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_completed_download_is_announced_with_a_success_icon_not_an_info_icon()
    {
        if (!OperatingSystem.IsLinux())
            return; // the icon argument only exists on the freedesktop path

        NotificationService.Enabled = true;
        NotificationService.NotifyCompleted("holiday.zip");

        var (file, args) = Assert.Single(_ran);
        Assert.Equal("notify-send", file);
        // A finished download gets the green check, never the blue "i" (#3).
        Assert.Contains("emblem-default", args);
        Assert.DoesNotContain("dialog-information", args);
        Assert.Contains(args, a => a.Contains("holiday.zip"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_failure_is_announced_with_the_error_icon_and_the_reason()
    {
        if (!OperatingSystem.IsLinux())
            return;

        NotificationService.Enabled = true;
        NotificationService.NotifyFailed("holiday.zip", "server said no");

        var (_, args) = Assert.Single(_ran);
        Assert.Contains("dialog-error", args);
        Assert.Contains(args, a => a.Contains("server said no"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_nameless_download_still_produces_a_readable_message()
    {
        if (!OperatingSystem.IsLinux())
            return;

        NotificationService.Enabled = true;
        NotificationService.NotifyCompleted(null);
        NotificationService.NotifyAllCompleted(0);

        Assert.Equal(2, _ran.Count);
        Assert.All(_ran, r => Assert.DoesNotContain(r.Args, a => a.Contains("()")));
    }

    // ---- the file logger ---------------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Nothing_is_written_while_logging_is_off()
    {
        AppLog.SetEnabled(false);
        Assert.False(AppLog.IsEnabled);

        var before = File.Exists(AppLog.CurrentLogFile) ? new FileInfo(AppLog.CurrentLogFile).Length : 0;

        AppLog.Info("should not appear");
        AppLog.Warn("should not appear");
        AppLog.Error("should not appear", new InvalidOperationException("nope"));

        var after = File.Exists(AppLog.CurrentLogFile) ? new FileInfo(AppLog.CurrentLogFile).Length : 0;
        Assert.Equal(before, after);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Enabling_logging_writes_each_level_and_the_exception_detail()
    {
        AppLog.SetEnabled(true);
        Assert.True(AppLog.IsEnabled);

        var marker = Guid.NewGuid().ToString("N");
        AppLog.Info("info-" + marker);
        AppLog.Warn("warn-" + marker);
        AppLog.Error("error-" + marker, new InvalidOperationException("boom-" + marker));

        var text = File.ReadAllText(AppLog.CurrentLogFile);
        Assert.Contains("info-" + marker, text);
        Assert.Contains("warn-" + marker, text);
        Assert.Contains("error-" + marker, text);
        // The exception type and message are what make a log actionable.
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("boom-" + marker, text);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_engine_logger_bridge_writes_into_the_same_file()
    {
        AppLog.SetEnabled(true);
        var marker = Guid.NewGuid().ToString("N");

        // The Downloader engine takes an ILoggerFactory; its output has to land in OUR log or the
        // engine's diagnostics are invisible when a user sends logs in.
        var logger = AppLog.Factory.CreateLogger("test");
        logger.LogInformation("bridged-" + marker);

        Assert.Contains("bridged-" + marker, File.ReadAllText(AppLog.CurrentLogFile));

        // The bridge must tolerate the scope API even though it does not implement scopes.
        using (logger.BeginScope("scope")) { }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_null_message_or_exception_never_throws()
    {
        AppLog.SetEnabled(true);

        AppLog.Info(null);
        AppLog.Error("no exception attached", null);
        AppLog.Error(null, new Exception("only an exception"));
    }

    // ---- URL and file-name resolution -------------------------------------

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("https://host/path/file.zip", "file.zip")]
    [InlineData("https://host/path/file.zip?token=abc", "file.zip")]
    [InlineData("https://host/path/file.zip#frag", "file.zip")]
    [InlineData("https://host/a%20b.zip", "a b.zip")]
    [InlineData("https://host/", null)]
    [InlineData("https://host", null)]
    [InlineData("not a url", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void A_file_name_is_taken_from_the_url_path(string? url, string? expected)
    {
        // This is the name a queued download shows before the engine ever runs, so a wrong answer is
        // visible to the user immediately.
        Assert.Equal(expected, UrlResolver.NameFromUrl(url));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_name_from_a_url_is_always_a_bare_file_name()
    {
        // Whatever comes out is combined with the save folder to build a path, so it must never carry
        // a directory component. Note the sanitiser uses the PLATFORM's invalid-character set, so a
        // backslash is replaced on Windows (where it separates) and legitimately kept on Linux (where
        // it is an ordinary filename character) — hence the invariant is "no directory part on this
        // platform", not "no backslash anywhere".
        foreach (var url in new[]
                 {
                     "https://host/a/b/c.zip",
                     "https://host/..%2F..%2Fetc%2Fpasswd.zip",
                     "https://host/%5Cwindows%5Csystem32%5Cx.dll",
                     "https://host/deep/nested/path/file.tar.gz",
                 })
        {
            var name = UrlResolver.NameFromUrl(url);
            if (name == null)
                continue;

            Assert.DoesNotContain(Path.DirectorySeparatorChar, name);
            Assert.DoesNotContain(Path.AltDirectorySeparatorChar, name);
            Assert.Equal(name, Path.GetFileName(name));
        }
    }

    // ---- the notch overlay -------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_notch_overlay_starts_and_stops_cleanly()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        manager.Initialize(Config.New());

        try
        {
            NotchService.Start(manager);
            // Fail-soft by design: a platform that cannot create the borderless topmost window logs
            // and stays inactive rather than throwing, so both outcomes are acceptable — what must
            // not happen is an exception escaping.
            NotchService.Start(manager); // starting twice must not stack a second overlay
        }
        finally
        {
            NotchService.Stop();
            NotchService.Stop(); // idempotent
        }

        Assert.False(NotchService.IsActive);
    }

    // ---- confirmation dialog data -----------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_confirmation_model_carries_its_title_and_message()
    {
        var vm = new ConfirmViewModel("Replace the file?", "A different size means the partial is lost.");

        Assert.Equal("Replace the file?", vm.Title);
        Assert.Equal("A different size means the partial is lost.", vm.Message);
    }

    // ---- SDK default implementations --------------------------------------

    private sealed class MinimalResolver : ILinkResolver
    {
        public bool CanResolve(string url) => true;
        public Task<DownloadPlan> ResolveAsync(string url, System.Threading.CancellationToken ct) =>
            Task.FromResult(new DownloadPlan { Parts = new[] { new DownloadPart { Url = url } } });
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_resolver_gets_working_defaults_for_everything_it_does_not_implement()
    {
        ILinkResolver resolver = new MinimalResolver();

        // These defaults are the compatibility contract for external plugins: a plugin compiled
        // against an older SDK must keep working when the host starts calling the newer overloads.
        Assert.False(resolver.IsFallback);
        Assert.Null(await resolver.GetVariantsAsync("https://host/f", null, System.Threading.CancellationToken.None));

        var plan = await resolver.ResolveAsync("https://host/f", null, System.Threading.CancellationToken.None);
        Assert.Equal("https://host/f", Assert.Single(plan.Parts).Url);
    }
}
