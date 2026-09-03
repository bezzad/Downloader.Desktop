using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
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
/// One slow request must not take the whole local API down with it.
///
/// The accept loop used to await each request before accepting the next, so a single <c>/api/variants</c>
/// lookup — which runs the site tool and can take a minute, or wedge — held every other caller. Reported
/// from the browser extension: after a video page had been opened, the popup's status dot never turned
/// green (its <c>/ping</c> never answered) and clicking Download on any detected file left the button on
/// "…" for ever with nothing arriving in the app, because that add request was queued behind the lookup.
/// </summary>
public class ConcurrentApiRequestTests
{
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void A_slow_lookup_does_not_block_ping_or_add()
    {
        LocalApiService.Stop();

        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false; // nothing may hit the network here
        manager.Initialize(config);

        var resolver = new BlockingResolver();
        var plugins = new PluginManager();
        plugins.RegisterPlugin(new BlockingPlugin(resolver));

        LocalApiService.Manager = manager;
        LocalApiService.Config = config;
        LocalApiService.Plugins = plugins;
        try
        {
            LocalApiService.Start();
            Assert.True(LocalApiService.IsRunning);
            var port = LocalApiService.EffectivePort;
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

            // A lookup that has reached the site tool and is going nowhere.
            var slow = Task.Run(() => client.PostAsync(
                $"http://127.0.0.1:{port}/api/variants",
                new StringContent("""{"url":"https://videos.example/watch?v=abc"}""", Encoding.UTF8, "application/json")));
            Pump(() => resolver.Entered, "the resolver was never reached");

            // Everything else still works while it hangs: the health check the popup's dot reads…
            var ping = Send(() => client.GetAsync($"http://127.0.0.1:{port}/ping"));
            Assert.Equal(HttpStatusCode.OK, ping.StatusCode);

            // …and an add from another tab, which is the click that used to vanish.
            var add = Send(() => client.PostAsync(
                $"http://127.0.0.1:{port}/api/add",
                new StringContent("""{"url":"https://cdn.example/clip.mp4","start":false}""", Encoding.UTF8, "application/json")));
            Assert.Equal(HttpStatusCode.Created, add.StatusCode);
            Assert.Single(manager.Items);

            resolver.Release();
            var answered = Wait(slow);
            Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        }
        finally
        {
            resolver.Release();
            LocalApiService.Stop();
            LocalApiService.Manager = null;
            LocalApiService.Config = null;
            LocalApiService.Plugins = null;
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void A_lookup_that_never_answers_gives_up_on_its_own_deadline()
    {
        LocalApiService.Stop();

        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false;
        manager.Initialize(config);

        var resolver = new BlockingResolver();
        var plugins = new PluginManager();
        plugins.RegisterPlugin(new BlockingPlugin(resolver));

        LocalApiService.Manager = manager;
        LocalApiService.Config = config;
        LocalApiService.Plugins = plugins;
        var restore = LocalApiService.VariantLookupTimeout;
        LocalApiService.VariantLookupTimeout = TimeSpan.FromMilliseconds(300);
        try
        {
            LocalApiService.Start();
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

            // The tool is never released — the request still comes back, saying why, so the caller shows
            // the page as one plain download instead of waiting on an answer that will never arrive.
            var response = Send(() => client.PostAsync(
                $"http://127.0.0.1:{LocalApiService.EffectivePort}/api/variants",
                new StringContent("""{"url":"https://videos.example/watch?v=abc"}""", Encoding.UTF8, "application/json")));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = Wait(Task.Run(() => response.Content.ReadAsStringAsync()));
            Assert.Contains("\"variants\":[]", body);
            Assert.Contains("error", body);
            Assert.True(resolver.Cancelled, "the lookup was abandoned without cancelling the tool");
        }
        finally
        {
            LocalApiService.VariantLookupTimeout = restore;
            resolver.Release();
            LocalApiService.Stop();
            LocalApiService.Manager = null;
            LocalApiService.Config = null;
            LocalApiService.Plugins = null;
        }
    }

    // The handlers marshal onto the UI thread, which IS this thread under the headless runtime, so an
    // awaited request would deadlock — run it off-thread and pump (same pattern as VariantsEndpointTests).
    private static T Wait<T>(Task<T> task)
    {
        Pump(() => task.IsCompleted, "local API request did not finish");
        return task.GetAwaiter().GetResult();
    }

    private static T Send<T>(Func<Task<T>> start) => Wait(Task.Run(start));

    private static void Pump(Func<bool> until, string message)
    {
        // Deliberately far shorter than the test's own timeout: a request that is blocked behind another
        // one must fail this in seconds, not hang the suite until the runner kills it.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!until())
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
            if (DateTime.UtcNow > deadline) throw new TimeoutException(message);
        }
    }

    private sealed class BlockingResolver : ILinkResolver
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public volatile bool Entered;
        public volatile bool Cancelled;

        public void Release() => _gate.TrySetResult();

        public bool CanResolve(string url) => url.Contains("videos.example", StringComparison.Ordinal);

        public Task<DownloadPlan> ResolveAsync(string url, CancellationToken ct) =>
            Task.FromResult(new DownloadPlan
            {
                Parts = new[] { new DownloadPart { Url = "https://cdn/v.mp4", Kind = PartKind.Combined } },
            });

        public async Task<IReadOnlyList<LinkVariant>> GetVariantsAsync(
            string url, ResolveOptions options, CancellationToken ct)
        {
            Entered = true;
            try
            {
                await _gate.Task.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }
            return new[] { new LinkVariant { Id = "1080", Label = "1080p", IsDefault = true } };
        }
    }

    private sealed class BlockingPlugin : IDownloaderPlugin
    {
        private readonly ILinkResolver _resolver;
        public BlockingPlugin(ILinkResolver resolver) => _resolver = resolver;
        public string Id => "test.slow-page-video";
        public string Name => "Slow Page Video Plugin";
        public string Version => "1.0.0";
        public string Author => "tester";
        public string Description => "claims video page URLs and never answers";
        public void Initialize(IPluginContext context) => context.RegisterResolver(_resolver);
    }
}
