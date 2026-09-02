using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
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
/// <c>/api/variants</c> — the qualities behind a page, so the browser extension can offer the same picker
/// the Add window does. Sending a video page used to download whatever the site happened to be playing,
/// with no way to ask for audio-only or a smaller copy.
/// </summary>
public class VariantsEndpointTests
{
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void The_page_qualities_come_back_with_the_callers_session_attached()
    {
        LocalApiService.Stop();

        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false; // nothing may hit the network here
        manager.Initialize(config);

        var resolver = new VariantResolver();
        var plugins = new PluginManager();
        plugins.RegisterPlugin(new VariantPlugin(resolver));

        LocalApiService.Manager = manager;
        LocalApiService.Config = config;
        LocalApiService.Plugins = plugins;
        try
        {
            LocalApiService.Start();
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            var answer = Post(client, "/api/variants", """
                {"url":"https://videos.example/watch?v=abc",
                 "cookies":[{"name":"SID","value":"secret","domain":".videos.example","path":"/"}]}
                """);
            var variants = answer.GetProperty("variants");
            Assert.Equal(2, variants.GetArrayLength());
            Assert.Equal("1080", variants[0].GetProperty("id").GetString());
            Assert.True(variants[0].GetProperty("default").GetBoolean());
            Assert.Equal("audio", variants[1].GetProperty("id").GetString());
            Assert.Equal("Page Video Plugin", answer.GetProperty("by").GetString());

            // The session has to reach the resolver: a site that only answers a signed-in session lists
            // no qualities at all without it, which is exactly the case this endpoint exists for.
            Assert.NotNull(resolver.SeenCookieFile);
            Assert.Contains("secret", resolver.SeenCookieContent);
            // …and the temp jar must not outlive the request.
            Assert.False(File.Exists(resolver.SeenCookieFile));

            // A link nothing claims is an empty list, not an error — the caller shows one plain download.
            var unclaimed = Post(client, "/api/variants", """{"url":"https://news.example/article"}""");
            Assert.Empty(unclaimed.GetProperty("variants").EnumerateArray());

            // A lookup that FAILS answers 200 with the reason: the page can still be handed over whole
            // and the app picks a stream itself, so a failure here must not read as a broken request.
            resolver.Fail = true;
            var failed = Post(client, "/api/variants", """{"url":"https://videos.example/watch?v=abc"}""");
            Assert.Empty(failed.GetProperty("variants").EnumerateArray());
            Assert.Equal("the site refused", failed.GetProperty("error").GetString());

            Assert.Equal(HttpStatusCode.BadRequest, Send(client, "/api/variants", "{}").StatusCode);
        }
        finally
        {
            LocalApiService.Stop();
            LocalApiService.Manager = null;
            LocalApiService.Config = null;
            LocalApiService.Plugins = null;
        }
    }

    // Same off-thread request + dispatcher pumping as CanHandleEndpointTests (the handlers marshal onto
    // the UI thread, which IS this thread under the headless runtime).
    private static HttpResponseMessage Send(HttpClient client, string path, string body)
    {
        var task = Task.Run(() => client.PostAsync(
            $"http://127.0.0.1:{LocalApiService.EffectivePort}{path}",
            new StringContent(body, Encoding.UTF8, "application/json")));
        var deadline = DateTime.UtcNow.AddSeconds(40);
        while (!task.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
            if (DateTime.UtcNow > deadline) throw new TimeoutException("local API request did not finish");
        }
        return task.GetAwaiter().GetResult();
    }

    private static JsonElement Post(HttpClient client, string path, string body)
    {
        var response = Send(client, path, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = Task.Run(() => response.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private sealed class VariantResolver : ILinkResolver
    {
        public string SeenCookieFile { get; private set; }
        public string SeenCookieContent { get; private set; } = string.Empty;
        public bool Fail { get; set; }

        public bool CanResolve(string url) => url.Contains("videos.example", StringComparison.Ordinal);

        public Task<DownloadPlan> ResolveAsync(string url, CancellationToken ct) =>
            Task.FromResult(new DownloadPlan
            {
                Parts = new[] { new DownloadPart { Url = "https://cdn/v.mp4", Kind = PartKind.Combined } },
            });

        public Task<IReadOnlyList<LinkVariant>> GetVariantsAsync(
            string url, ResolveOptions options, CancellationToken ct)
        {
            if (Fail) throw new InvalidOperationException("the site refused");
            if (options?.CookieFilePath is { } path)
            {
                SeenCookieFile = path;
                SeenCookieContent = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            }
            return Task.FromResult<IReadOnlyList<LinkVariant>>(new[]
            {
                new LinkVariant { Id = "1080", Label = "1080p", ExpectedSize = 120_000_000, IsDefault = true },
                new LinkVariant { Id = "audio", Label = "Audio only", ExpectedSize = 4_000_000 },
            });
        }
    }

    private sealed class VariantPlugin : IDownloaderPlugin
    {
        private readonly ILinkResolver _resolver;
        public VariantPlugin(ILinkResolver resolver) => _resolver = resolver;
        public string Id => "test.page-video";
        public string Name => "Page Video Plugin";
        public string Version => "1.0.0";
        public string Author => "tester";
        public string Description => "claims video page URLs and offers qualities";
        public void Initialize(IPluginContext context) => context.RegisterResolver(_resolver);
    }
}
