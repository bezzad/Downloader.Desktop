using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>The detailed log (<c>detailed-logging</c>): what it writes, what it never writes, how long it
/// keeps files and how it exports them.</summary>
public class DetailedLoggingTests
{
    // ---- URL sanitiser ----------------------------------------------------

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("https://cdn.example.com/f.zip?token=abc#frag", "https://cdn.example.com/f.zip")]
    [InlineData("https://user:pass@host.example/a/b", "https://host.example/a/b")]
    [InlineData("http://[::1]:8080/x?y=1", "http://[::1]:8080/x")]
    [InlineData("http://host.example:80/x", "http://host.example/x")]
    [InlineData("not a url", "<url>")]
    [InlineData("", "<url>")]
    public void Urls_lose_their_query_fragment_and_user_info(string input, string expected) =>
        Assert.Equal(expected, LogText.Url(input));

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_proxy_password_is_masked() =>
        Assert.Equal("http://***@proxy:8080", LogText.MaskUserInfo("http://user:pass@proxy:8080"));

    // ---- on / off ---------------------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Nothing_is_written_while_logging_is_off()
    {
        using var log = new LogScope(enable: false);
        AppLog.Info("hello");
        AppLog.Error("boom", new InvalidOperationException("x"));
        Assert.Empty(Directory.GetFiles(log.Folder));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_error_keeps_its_full_stack_trace()
    {
        using var log = new LogScope();
        try { ThrowSomething(); }
        catch (Exception ex) { AppLog.Error("It failed", ex); }

        var text = log.Text();
        Assert.Contains("It failed", text);
        Assert.Contains("System.InvalidOperationException: deep problem", text);
        Assert.Contains(nameof(ThrowSomething), text); // a stack frame, not just the message
    }

    private static void ThrowSomething() => throw new InvalidOperationException("deep problem");

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_engine_logger_writes_debug_lines_and_stack_traces()
    {
        using var log = new LogScope();
        var logger = AppLog.Factory.CreateLogger("Engine.Test");
        Microsoft.Extensions.Logging.LoggerExtensions.LogDebug(logger, "debug detail");
        try { ThrowSomething(); }
        catch (Exception ex) { Microsoft.Extensions.Logging.LoggerExtensions.LogError(logger, ex, "engine error"); }

        var text = log.Text();
        Assert.Contains("[DEBUG] [Test] debug detail", text);
        Assert.Contains(nameof(ThrowSomething), text);
    }

    // ---- retention --------------------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Old_log_files_are_deleted_and_recent_and_foreign_files_kept()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dldesktop-prune-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var now = new DateTime(2026, 9, 28, 10, 0, 0);
            string Make(string name) { var p = Path.Combine(dir, name); File.WriteAllText(p, "x"); return p; }
            var old = Make("downloader-2026-09-19.log");    // 9 days old
            var recent = Make("downloader-2026-09-25.log"); // 3 days old
            var foreign = Make("notes.txt");
            var oddName = Make("downloader-latest.log");     // not dated → not ours to judge

            AppLog.Prune(dir, now, AppLog.KeepDays);

            Assert.False(File.Exists(old));
            Assert.True(File.Exists(recent));
            Assert.True(File.Exists(foreign));
            Assert.True(File.Exists(oddName));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Opening_the_log_removes_files_older_than_a_week()
    {
        using var log = new LogScope();
        var stale = Path.Combine(log.Folder, $"downloader-{DateTime.Now.AddDays(-10):yyyy-MM-dd}.log");
        File.WriteAllText(stale, "old");
        AppLog.SetEnabled(false); // the next write opens the file anew, which is when pruning runs
        AppLog.SetEnabled(true);
        Assert.False(File.Exists(stale));
    }

    // ---- export -----------------------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Export_saves_every_kept_file_in_one_zip()
    {
        using var log = new LogScope();
        File.WriteAllText(Path.Combine(log.Folder, $"downloader-{DateTime.Now.AddDays(-2):yyyy-MM-dd}.log"), "a");
        File.WriteAllText(Path.Combine(log.Folder, $"downloader-{DateTime.Now.AddDays(-1):yyyy-MM-dd}.log"), "b");
        AppLog.Info("today"); // today's file, still held open by the writer

        var zipPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            Assert.Equal(3, AppLog.ExportZip(zipPath));
            using var zip = ZipFile.OpenRead(zipPath);
            Assert.Equal(3, zip.Entries.Count);
            using var today = new StreamReader(zip.GetEntry(Path.GetFileName(AppLog.CurrentLogFile))!.Open());
            Assert.Contains("today", today.ReadToEnd());
        }
        finally { File.Delete(zipPath); }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Export_with_no_log_writes_nothing()
    {
        using var log = new LogScope(enable: false);
        var zipPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        Assert.Equal(0, AppLog.ExportZip(zipPath));
        Assert.False(File.Exists(zipPath));
    }

    // ---- settings diff + header -------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_changed_number_is_logged_as_old_to_new()
    {
        var before = DownloadSettings.New();
        before.MaxConcurrentDownloads = 3;
        var after = SettingsDiff.Snapshot(before);
        after.MaxConcurrentDownloads = 5;

        Assert.Equal(["MaxConcurrentDownloads: 3 → 5"], SettingsDiff.Compute(before, after));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_unchanged_save_logs_nothing()
    {
        var s = DownloadSettings.New();
        Assert.Empty(SettingsDiff.Compute(s, SettingsDiff.Snapshot(s)));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_proxy_change_hides_the_user_name_and_password()
    {
        var before = DownloadSettings.New();
        var after = SettingsDiff.Snapshot(before);
        after.ProxyAddress = "http://user:pass@proxy:8080";

        var line = Assert.Single(SettingsDiff.Compute(before, after));
        Assert.Contains("http://***@proxy:8080", line);
        Assert.DoesNotContain("pass", line.Replace("ProxyAddress", ""));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Reset_to_defaults_lists_each_changed_value_once()
    {
        var custom = DownloadSettings.New();
        custom.ChunkCount = 2;
        custom.Language = "fa";
        custom.EnableLogging = true;

        var lines = SettingsDiff.Compute(custom, DownloadSettings.New());

        Assert.Equal(3, lines.Count);
        Assert.Contains("ChunkCount: 2 → 8", lines);
        Assert.Contains("Language: fa → en", lines);
        Assert.Contains("EnableLogging: True → False", lines);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_session_header_names_the_version_system_and_settings_without_secrets()
    {
        var config = Config.New();
        config.Settings.Language = "de";
        config.Settings.ProxyAddress = "socks5://me:secret@127.0.0.1:1080";

        var header = SessionHeader.Build(config);

        Assert.Contains($"App version: {UpdateService.CurrentVersion}", header);
        Assert.Contains("OS: ", header);
        Assert.Contains("Runtime: .NET", header);
        Assert.Contains("Language: de", header);
        Assert.Contains("Theme: ", header);
        Assert.Contains("ChunkCount=8", header);
        Assert.DoesNotContain("secret", header);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_header_is_written_first_as_separate_lines()
    {
        using var log = new LogScope();
        AppLog.WriteHeader(SessionHeader.Build(Config.New()));
        var lines = log.Text().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(lines, l => l.Contains("===== Session start ====="));
        Assert.Contains(lines, l => l.Contains("[INFO] Runtime: "));
    }
}
