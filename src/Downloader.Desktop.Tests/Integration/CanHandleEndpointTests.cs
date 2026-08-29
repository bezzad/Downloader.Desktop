using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// <c>/api/can-handle</c> — the answer the browser extension needs before it tells a user a site is a dead
/// end. Whether a YouTube page is downloadable depends on which plugins THIS install has enabled, which
/// only the app knows; the extension previously hard-coded "unsupported" and was wrong for anyone who had
/// installed the site-media plugin (issue #9 follow-up).
/// </summary>
public class CanHandleEndpointTests
{
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_app_reports_whether_a_plugin_claims_a_page_and_which_one()
    {
        LocalApiService.Stop(); // a leak from another test would answer on the wrong port

        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false; // nothing may hit the network here
        manager.Initialize(config);

        var plugins = new PluginManager();
        plugins.RegisterPlugin(new PageResolvingPlugin());

        LocalApiService.Manager = manager;
        LocalApiService.Config = config;
        LocalApiService.Plugins = plugins;
        LocalApiService.Start();
        Assert.True(LocalApiService.IsRunning);

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            var claimed = Json(client, "/api/can-handle?url=" + Uri.EscapeDataString("https://videos.example/watch?v=abc"));
            Assert.True(claimed.GetProperty("handled").GetBoolean());
            Assert.Equal("Page Video Plugin", claimed.GetProperty("by").GetString());

            var notClaimed = Json(client, "/api/can-handle?url=" + Uri.EscapeDataString("https://news.example/article"));
            Assert.False(notClaimed.GetProperty("handled").GetBoolean());
            Assert.Equal(JsonValueKind.Null, notClaimed.GetProperty("by").ValueKind);

            // A disabled plugin must not be reported as able to handle anything — the extension would
            // then offer a page the app is going to refuse.
            plugins.SetEnabled("test.page-video", false);
            var disabled = Json(client, "/api/can-handle?url=" + Uri.EscapeDataString("https://videos.example/watch?v=abc"));
            Assert.False(disabled.GetProperty("handled").GetBoolean());

            Assert.Equal(HttpStatusCode.BadRequest, Send(client, "/api/can-handle").StatusCode);
        }
        finally
        {
            LocalApiService.Stop();
            LocalApiService.Manager = null;
            LocalApiService.Config = null;
            LocalApiService.Plugins = null;
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void With_no_plugin_system_the_answer_is_a_plain_no_not_an_error()
    {
        LocalApiService.Stop();

        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false;
        manager.Initialize(config);

        LocalApiService.Manager = manager;
        LocalApiService.Config = config;
        LocalApiService.Plugins = null; // as during startup, before plugins are wired
        LocalApiService.Start();

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var answer = Json(client, "/api/can-handle?url=" + Uri.EscapeDataString("https://videos.example/watch?v=abc"));
            Assert.False(answer.GetProperty("handled").GetBoolean());
        }
        finally
        {
            LocalApiService.Stop();
            LocalApiService.Manager = null;
            LocalApiService.Config = null;
        }
    }

    // The /api handlers marshal onto the UI thread, which IS this thread under the headless runtime —
    // so the request runs off-thread while the dispatcher is pumped (see LocalApiEndToEndTests.Pump).
    private static HttpResponseMessage Send(HttpClient client, string pathAndQuery)
    {
        var task = Task.Run(() => client.GetAsync($"http://127.0.0.1:{LocalApiService.EffectivePort}{pathAndQuery}"));
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
            if (DateTime.UtcNow > deadline) throw new TimeoutException("local API request did not finish");
        }
        return task.GetAwaiter().GetResult();
    }

    private static JsonElement Json(HttpClient client, string pathAndQuery)
    {
        var response = Send(client, pathAndQuery);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = Task.Run(() => response.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private sealed class PageResolver : ILinkResolver
    {
        public bool CanResolve(string url) => url.Contains("videos.example", StringComparison.Ordinal);
        public Task<DownloadPlan> ResolveAsync(string url, CancellationToken ct) =>
            Task.FromResult(new DownloadPlan
            {
                SuggestedFileName = "video.mp4",
                Parts = new[] { new DownloadPart { Url = "https://cdn/v.mp4", Kind = PartKind.Combined } },
                PostProcess = PostProcess.None,
            });
    }

    private sealed class PageResolvingPlugin : IDownloaderPlugin
    {
        public string Id => "test.page-video";
        public string Name => "Page Video Plugin";
        public string Version => "1.0.0";
        public string Author => "tester";
        public string Description => "claims video page URLs";
        public void Initialize(IPluginContext context) => context.RegisterResolver(new PageResolver());
    }
}
