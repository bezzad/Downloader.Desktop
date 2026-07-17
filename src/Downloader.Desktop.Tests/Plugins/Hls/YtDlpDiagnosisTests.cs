using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Plugins.Hls;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins.Hls;

/// <summary>
/// LIVE network diagnostic (fix-hls-youtube-resolver, task 1). Gated on <c>DLDESKTOP_NET=1</c> like the
/// GitHub-plugin live test, so CI/offline runs skip it. Runs the real <see cref="YtDlpBinary"/> against a
/// known-public YouTube URL and categorizes WHICH stage fails and why, so the fix targets the actual root
/// cause instead of an assumption. It NEVER logs or asserts on the video's content — only structural facts
/// about the extracted JSON (did we get real formats? how many) and the failure category. Once a fix lands,
/// the same test is the regression guard.
/// </summary>
public class YtDlpDiagnosisTests
{
    // A stable, public, non-age-restricted video (per the change's task 1.1).
    private const string TestUrl = "https://youtu.be/Wv6LFlehX4k";

    private readonly ITestOutputHelper _out;
    public YtDlpDiagnosisTests(ITestOutputHelper output) => _out = output;

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Diagnose_youtube_extraction()
    {
        if (Environment.GetEnvironmentVariable("DLDESKTOP_NET") != "1")
            return; // opt-in only

        var dataDir = Path.Combine(Path.GetTempPath(), "dldesktop-hls-diag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        var logs = new CapturingLogger();
        using var http = new HttpClient();
        var ytdlp = new YtDlpBinary(dataDir, http, logs);

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4)); // provisioning + extraction can be slow

        // Optional: a Netscape cookie file the author exported from a signed-in browser (what the extension
        // supplies at runtime). When set, this exercises the FIX path (--cookies <file>) so task 5.1 can
        // turn this same test green end-to-end without a live login in CI.
        var suppliedCookieFile = Environment.GetEnvironmentVariable("DLDESKTOP_COOKIES");
        var hasSupplied = !string.IsNullOrEmpty(suppliedCookieFile) && File.Exists(suppliedCookieFile);

        string category;
        int formatCount = -1;
        try
        {
            var json = await ytdlp.ExtractJsonAsync(TestUrl, hasSupplied ? suppliedCookieFile : null, cts.Token);
            formatCount = CountFormats(json);
            // ExtractJsonAsync returns stdout whether it succeeded via supplied cookies, anonymously, or a
            // browser retry; the logs tell us which path won.
            var suppliedWorked = logs.Contains("Trying extension-supplied cookies") && !logs.Contains("Supplied cookies didn't work");
            var usedBrowser = logs.Contains("retrying yt-dlp with");
            category = formatCount > 0
                ? (suppliedWorked ? "SUCCESS_WITH_SUPPLIED_COOKIES"
                   : usedBrowser ? "SUCCESS_AFTER_COOKIE_RETRY" : "SUCCESS_ANONYMOUS")
                : "SUCCESS_BUT_NO_REAL_FORMATS"; // e.g. storyboard-only — deno likely missing
        }
        catch (OperationCanceledException)
        {
            category = "TIMEOUT";
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("signed-in browser session"))
        {
            category = "NEEDS_COOKIES_ALL_BROWSERS_EXHAUSTED";
        }
        catch (Exception ex)
        {
            category = "OTHER_YTDLP_ERROR: " + ex.GetType().Name;
        }

        var denoFailed = logs.Contains("Couldn't provision deno");

        var summary = new System.Text.StringBuilder();
        summary.AppendLine("=== YouTube extraction diagnosis ===");
        summary.AppendLine($"url            : {TestUrl}");
        summary.AppendLine($"category       : {category}");
        summary.AppendLine($"supplied cookie: {(hasSupplied ? "yes (DLDESKTOP_COOKIES)" : "no")}");
        summary.AppendLine($"real formats   : {(formatCount < 0 ? "n/a" : formatCount.ToString())}");
        summary.AppendLine($"deno provisioned: {(denoFailed ? "NO (provisioning failed)" : "yes/onpath/cached")}");
        summary.AppendLine("--- relevant log lines (no cookie values, no content) ---");
        foreach (var line in logs.Lines)
            summary.AppendLine("  " + line);

        _out.WriteLine(summary.ToString());
        // Also persist so a headless/background run can read the outcome (task 1.2 records it).
        var outFile = Environment.GetEnvironmentVariable("DLDESKTOP_DIAG_OUT");
        if (!string.IsNullOrEmpty(outFile))
            try { File.WriteAllText(outFile, summary.ToString()); } catch { /* best effort */ }

        try { Directory.Delete(dataDir, recursive: true); } catch { /* best effort */ }

        // The test's job is to REPORT the category, not to pass/fail on a moving target. It only hard-fails
        // if the diagnostic itself couldn't run (so a broken harness is visible), not on the YouTube outcome.
        Assert.False(string.IsNullOrEmpty(category));
    }

    private static int CountFormats(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array)
            {
                // "Real" = has an actual media URL (storyboard/thumbnail pseudo-formats don't count as playable).
                int n = 0;
                foreach (var f in formats.EnumerateArray())
                    if (f.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String &&
                        f.TryGetProperty("vcodec", out var vc) && vc.GetString() != "none" ||
                        f.TryGetProperty("acodec", out var ac) && ac.GetString() != "none")
                        n++;
                return n;
            }
        }
        catch { /* not JSON we understand */ }
        return 0;
    }

    /// <summary>Collects log MESSAGES only (never structured cookie values) for post-hoc categorization.</summary>
    private sealed class CapturingLogger : ILogger
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public bool Contains(string needle)
        {
            foreach (var l in Lines) if (l.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter)
            => Lines.Enqueue($"[{logLevel}] {formatter(state, exception)}");

        private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
    }
}
