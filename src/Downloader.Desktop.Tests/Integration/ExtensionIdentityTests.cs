using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// The extension telling the app which version is installed, so the app can say "your Chrome extension is
/// out of date" instead of leaving a user on a build that silently stopped working.
///
/// It travels on requests the extension already makes, so the two rules under test are: it must actually
/// arrive, and a request without it must behave exactly as before (an older extension, the CLI, and any
/// other local tool all send nothing).
/// </summary>
public class ExtensionIdentityTests
{
    // ---- the pure parser: all three carriers, without a listener ----

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_query_pair_is_read()
    {
        var id = LocalApiService.ParseExtensionIdentity("1.7.0", "chrome", null);

        Assert.NotNull(id);
        Assert.Equal("1.7.0", id.Version);
        Assert.Equal("chrome", id.Browser);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_header_is_read_when_the_query_carries_nothing()
    {
        var id = LocalApiService.ParseExtensionIdentity(null, null, "1.8.0; firefox");

        Assert.NotNull(id);
        Assert.Equal("1.8.0", id.Version);
        Assert.Equal("firefox", id.Browser);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_query_wins_over_the_header()
    {
        var id = LocalApiService.ParseExtensionIdentity("2.0.0", "edge", "1.0.0; chrome");

        Assert.Equal("2.0.0", id.Version);
        Assert.Equal("edge", id.Browser);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_version_with_no_browser_is_still_usable()
        => Assert.Equal("unknown", LocalApiService.ParseExtensionIdentity("1.7.0", null, null).Browser);

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(null, null, null)]
    [InlineData("", "", "")]
    [InlineData("   ", null, null)]
    [InlineData(null, "chrome", null)]      // a browser with no version says nothing useful
    [InlineData(null, null, "; chrome")]
    public void Nothing_usable_reported_reads_as_an_older_extension(string? version, string? browser, string? header)
        => Assert.Null(LocalApiService.ParseExtensionIdentity(version, browser, header));

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_reported_value_is_treated_as_untrusted_text()
    {
        // It is rendered in Settings, so it must not carry newlines, and it must not be unbounded.
        Assert.Null(LocalApiService.ParseExtensionIdentity("1.7.0\nInjected: yes", null, null));
        Assert.Null(LocalApiService.ParseExtensionIdentity(null, null, "1.7.0\r\nX: y; chrome"));
        Assert.Equal(40, LocalApiService.ParseExtensionIdentity(new string('9', 300), "chrome", null).Version.Length);
    }

    // ---- end to end through the real listener ----

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void An_identified_request_is_recorded_and_an_anonymous_one_changes_nothing()
    {
        LocalApiService.Stop();              // a leak from another test would answer on the wrong port
        LocalApiService.ClearSeenExtensions();

        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false;   // nothing may hit the network here
        manager.Initialize(config);
        LocalApiService.Manager = manager;
        LocalApiService.Config = config;

        try
        {
            LocalApiService.Start();
            Assert.True(LocalApiService.IsRunning);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            // /ping is the cheapest carrier there is — the popup calls it to draw its status dot.
            Send(client, "/ping?extv=1.7.0&extb=chrome");
            var chrome = LocalApiService.LastSeenExtension("chrome");
            Assert.NotNull(chrome);
            Assert.Equal("1.7.0", chrome.Version);

            // A second browser is tracked separately: they are installed and updated independently.
            Send(client, "/ping", ("X-Downloader-Extension", "1.6.0; firefox"));
            Assert.Equal("1.6.0", LocalApiService.LastSeenExtension("firefox").Version);
            Assert.Equal("1.7.0", LocalApiService.LastSeenExtension("chrome").Version);

            // An upgrade is reflected rather than appended.
            Send(client, "/ping?extv=1.8.0&extb=chrome");
            Assert.Equal("1.8.0", LocalApiService.LastSeenExtension("chrome").Version);
            Assert.Equal(2, LocalApiService.LastSeenExtensions.Count);

            // An anonymous request — an older extension, or the CLI — is served exactly as before and
            // does not disturb what is already recorded.
            Assert.Equal(HttpStatusCode.OK, Send(client, "/ping").StatusCode);
            Assert.Equal(2, LocalApiService.LastSeenExtensions.Count);
            Assert.Null(LocalApiService.LastSeenExtension("safari"));

            // The /api routes carry it too, and still do their own job.
            Send(client, "/api/settings?extv=1.9.0&extb=edge");
            Assert.Equal("1.9.0", LocalApiService.LastSeenExtension("edge").Version);
        }
        finally
        {
            LocalApiService.Stop();          // unconditionally: a conditional restore preserves a leak
            LocalApiService.Manager = null;
            LocalApiService.Config = null;
            LocalApiService.ClearSeenExtensions();
        }
    }

    /// <summary>
    /// A reported identity is a secret-adjacent fact about the user's machine, and the config file is
    /// read, shipped and pasted into issues. It lives in memory only.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void A_reported_identity_never_reaches_the_config_file()
    {
        LocalApiService.Stop();
        LocalApiService.ClearSeenExtensions();

        var path = Path.Combine(Path.GetTempPath(), $"dldesktop-ident-{Guid.NewGuid():N}.json");
        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false;
        manager.Initialize(config);
        LocalApiService.Manager = manager;
        LocalApiService.Config = config;
        FileService.ConfigFileOverride = path;

        try
        {
            LocalApiService.Start();
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            Send(client, "/ping?extv=9.9.9&extb=chrome");
            Assert.Equal("9.9.9", LocalApiService.LastSeenExtension("chrome").Version);

            Task.Run(() => new FileService().SaveToFileAsync(config)).GetAwaiter().GetResult();

            var saved = File.ReadAllText(path);
            Assert.DoesNotContain("9.9.9", saved);
            Assert.DoesNotContain("extVersion", saved, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("LastSeenExtension", saved, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            LocalApiService.Stop();
            LocalApiService.Manager = null;
            LocalApiService.Config = null;
            LocalApiService.ClearSeenExtensions();
            FileService.ConfigFileOverride = null;
            try { File.Delete(path); } catch { }
        }
    }

    // The /api handlers marshal onto the UI thread, which IS this thread under the headless runtime, so
    // the request runs off-thread while the dispatcher is pumped (see LocalApiEndToEndTests.Pump).
    private static HttpResponseMessage Send(HttpClient client, string pathAndQuery,
        params (string Name, string Value)[] headers)
    {
        var task = Task.Run(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"http://127.0.0.1:{LocalApiService.EffectivePort}{pathAndQuery}");
            foreach (var (name, value) in headers)
                req.Headers.TryAddWithoutValidation(name, value);
            return client.SendAsync(req);
        });

        var deadline = DateTime.UtcNow.AddSeconds(40);
        while (!task.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
            if (DateTime.UtcNow > deadline) throw new TimeoutException("local API request did not finish");
        }
        return task.GetAwaiter().GetResult();
    }
}
