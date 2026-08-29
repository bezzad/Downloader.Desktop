using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
// Alias, not a plain using: this project's own Tests.Plugins namespace shadows the unqualified
// "Plugins." prefix, so Plugins.Ollama would resolve to the wrong place.
using OllamaPlugins = Downloader.Desktop.Plugins.Ollama;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// The "Add to Ollama" offer after a model download finishes — reported missing (issue #9 round).
/// <para>
/// A download can complete by three different routes (the plain engine, the multi-part plan runner, and a
/// plugin transfer) and the offer has to appear on all of them, because which route a model takes is an
/// implementation detail the user never sees. Each route is driven here for real, against a loopback
/// server, with a plugin that resolves the link and offers an action — the same shape the Ollama plugin
/// has. A route that forgets to offer fails here rather than in someone's download list.
/// </para>
/// </summary>
public class PostDownloadOfferTests
{
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task The_offer_appears_on_a_download_the_engine_completed()
    {
        using var server = new TinyServer(new Dictionary<string, byte[]> { ["/model.gguf"] = Bytes(4096) });
        var folder = TempDir();
        var plugins = PluginsThatResolveAndOffer(server.Url + "model.gguf");
        var manager = NewManager(plugins);

        var item = new DownloadItem { Url = "fakemodel:demo", SaveFolder = folder, FileName = "model.gguf" };
        manager.Add(item, autoStart: true);
        var vm = manager.Items[0];

        await WaitFor(() => vm.Status == global::Downloader.DownloadStatus.Completed);

        Assert.Equal("test.model-plugin", item.ResolverPluginId);
        Assert.Equal("Add to Test Store", manager.PostDownloadActionLabel(vm));
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task The_offer_appears_on_a_download_the_plan_runner_completed()
    {
        using var server = new TinyServer(new Dictionary<string, byte[]>
        {
            ["/part1"] = Bytes(2048),
            ["/part2"] = Bytes(2048),
        });
        var folder = TempDir();
        // Two parts ⇒ the plan runner owns completion, a different code path from the engine's.
        var plugins = PluginsThatResolveAndOffer(server.Url + "part1", server.Url + "part2");
        var manager = NewManager(plugins);

        var item = new DownloadItem { Url = "fakemodel:multipart", SaveFolder = folder, FileName = "model.gguf" };
        manager.Add(item, autoStart: true);
        var vm = manager.Items[0];

        await WaitFor(() => vm.Status == global::Downloader.DownloadStatus.Completed);

        Assert.Equal("test.model-plugin", item.ResolverPluginId);
        Assert.Equal("Add to Test Store", manager.PostDownloadActionLabel(vm));
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task The_offer_appears_on_a_download_a_plugin_transfer_completed()
    {
        var folder = TempDir();
        var plugins = new PluginManager();
        plugins.RegisterPlugin(new TransferModelPlugin());
        var manager = NewManager(plugins);

        var item = new DownloadItem { Url = "fakemodel://transfer/demo", SaveFolder = folder };
        manager.Add(item, autoStart: true);
        var vm = manager.Items[0];

        await WaitFor(() => vm.Status == global::Downloader.DownloadStatus.Completed);

        Assert.Equal("test.transfer-plugin", item.ResolverPluginId);
        Assert.Equal("Add to Test Store", manager.PostDownloadActionLabel(vm));
    }

    /// <summary>The offer must still be there after a restart: the app is closed with a finished model in
    /// the list and reopened, which rebuilds every row from the saved record alone.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task The_offer_survives_a_restart()
    {
        using var server = new TinyServer(new Dictionary<string, byte[]> { ["/model.gguf"] = Bytes(4096) });
        var folder = TempDir();
        var plugins = PluginsThatResolveAndOffer(server.Url + "model.gguf");

        var config = Config.New();
        var first = NewManager(plugins, config);
        first.Add(new DownloadItem { Url = "fakemodel:demo", SaveFolder = folder, FileName = "model.gguf" },
            autoStart: true);
        await WaitFor(() => first.Items[0].Status == global::Downloader.DownloadStatus.Completed);

        // What a restart really is: the saved records, read back into a fresh manager.
        var saved = first.Items[0].GetItem();
        var reopened = Config.New();
        reopened.Downloads = new List<DownloadItem> { RoundTrip(saved) };
        var second = NewManager(plugins, reopened);

        var restored = second.Items[0];
        Assert.Equal(global::Downloader.DownloadStatus.Completed, restored.Status);
        Assert.Equal("test.model-plugin", restored.GetItem().ResolverPluginId);
        Assert.Equal("Add to Test Store", second.PostDownloadActionLabel(restored));
    }

    /// <summary>The shipping Ollama resolver and the shipping "Add to Ollama" action, end to end through
    /// the engine route: what the reporter actually did. Only the registry is stubbed — it points the blob
    /// at a loopback server — so the URL the record keeps, the file the download lands on, and the action's
    /// own CanOffer are all the real ones.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task The_offer_appears_for_the_real_ollama_plugin()
    {
        using var server = new TinyServer(new Dictionary<string, byte[]> { ["/blob"] = Bytes(4096) });
        var folder = TempDir();
        var plugins = new PluginManager();
        plugins.RegisterPlugin(new RealOllamaPlugin(new BlobRegistry(server.Url + "blob")));
        var manager = NewManager(plugins);

        // A tagged model reference, exactly as the Add window stores it.
        var item = new DownloadItem { Url = "gemma3:12b", SaveFolder = folder, FileName = "gemma3-12b.gguf" };
        manager.Add(item, autoStart: true);
        var vm = manager.Items[0];

        await WaitFor(() => vm.Status == global::Downloader.DownloadStatus.Completed);

        Assert.Equal("com.bezzad.ollama-models", item.ResolverPluginId);
        Assert.Equal("Add to Ollama", manager.PostDownloadActionLabel(vm));
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Serialize + deserialize the record exactly as the config file does, so a field that only
    /// lives in memory (and would be gone after a real restart) cannot pass this test.</summary>
    private static DownloadItem RoundTrip(DownloadItem item)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(item);
        return System.Text.Json.JsonSerializer.Deserialize<DownloadItem>(json);
    }

    private static DownloadManager NewManager(PluginManager plugins, Config config = null)
    {
        var manager = new DownloadManager(plugins);
        manager.Initialize(config ?? Config.New());
        return manager;
    }

    private static PluginManager PluginsThatResolveAndOffer(params string[] partUrls)
    {
        var plugins = new PluginManager();
        plugins.RegisterPlugin(new ModelPlugin(partUrls));
        return plugins;
    }

    private static byte[] Bytes(int n)
    {
        var data = new byte[n];
        for (var i = 0; i < n; i++) data[i] = (byte)(i % 251);
        return data;
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dldesktop-offer-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(condition(), "the download never reached the expected state");
    }

    // ── the plugin under test's shape (mirrors the Ollama plugin: resolver + post-download action) ────

    private sealed class ModelResolver(string[] partUrls) : ILinkResolver
    {
        public bool CanResolve(string url) => url.StartsWith("fakemodel:", StringComparison.Ordinal);

        public Task<DownloadPlan> ResolveAsync(string url, CancellationToken ct)
        {
            var parts = new List<DownloadPart>();
            foreach (var partUrl in partUrls)
                parts.Add(new DownloadPart { Url = partUrl, Kind = PartKind.Combined });
            return Task.FromResult(new DownloadPlan
            {
                SuggestedFileName = "model.gguf",
                Parts = parts,
                PostProcess = PostProcess.None,
            });
        }
    }

    /// <summary>Offers exactly when the Ollama action does: the source URL still parses as a model
    /// reference, and the downloaded file is really on disk.</summary>
    private sealed class AddToStoreAction : IPostDownloadAction
    {
        public string Label => "Add to Test Store";
        public bool CanOffer(string sourceUrl, string filePath) =>
            sourceUrl != null && sourceUrl.StartsWith("fakemodel:", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath);
        public Task ExecuteAsync(string sourceUrl, string filePath, IProgress<double> progress, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class ModelPlugin(string[] partUrls) : IDownloaderPlugin
    {
        public string Id => "test.model-plugin";
        public string Name => "Model Plugin";
        public string Version => "1.0.0";
        public string Author => "tester";
        public string Description => "resolves a model reference and offers to install it";
        public void Initialize(IPluginContext context)
        {
            context.RegisterResolver(new ModelResolver(partUrls));
            context.RegisterPostDownloadAction(new AddToStoreAction());
        }
    }

    // ── the transfer route: a provider owns the whole download ────────────────────────────────────────

    private sealed class FileWritingTransfer(string targetFolder) : ITransfer
    {
        public event EventHandler<TransferProgress> ProgressChanged;

        public async Task<string> StartAsync(CancellationToken ct)
        {
            var path = Path.Combine(targetFolder, "model.gguf");
            await File.WriteAllBytesAsync(path, Bytes(1024), ct);
            ProgressChanged?.Invoke(this,
                new TransferProgress { BytesReceived = 1024, TotalBytes = 0, Percentage = 100 });
            return path;
        }

        public void Pause() { }
        public void Resume() { }
    }

    private sealed class ModelTransferProvider : ITransferProvider
    {
        public bool CanHandle(string url) => url.StartsWith("fakemodel://", StringComparison.Ordinal);
        public ITransfer Create(string url, string targetFolder) => new FileWritingTransfer(targetFolder);
    }

    private sealed class TransferAction : IPostDownloadAction
    {
        public string Label => "Add to Test Store";
        public bool CanOffer(string sourceUrl, string filePath) =>
            sourceUrl != null && sourceUrl.StartsWith("fakemodel://", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath);
        public Task ExecuteAsync(string sourceUrl, string filePath, IProgress<double> progress, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class TransferModelPlugin : IDownloaderPlugin
    {
        public string Id => "test.transfer-plugin";
        public string Name => "Transfer Model Plugin";
        public string Version => "1.0.0";
        public string Author => "tester";
        public string Description => "downloads a model itself and offers to install it";
        public void Initialize(IPluginContext context)
        {
            context.RegisterTransferProvider(new ModelTransferProvider());
            context.RegisterPostDownloadAction(new TransferAction());
        }
    }

    /// <summary>The shipping Ollama resolver + action, wired to a registry that serves one blob.</summary>
    private sealed class RealOllamaPlugin(OllamaPlugins.IOllamaRegistry registry) : IDownloaderPlugin
    {
        public string Id => "com.bezzad.ollama-models";
        public string Name => "Ollama Models";
        public string Version => "1.1.0";
        public string Author => "bezzad";
        public string Description => "the real resolver and action, with a stubbed registry";
        public void Initialize(IPluginContext context)
        {
            context.RegisterResolver(new OllamaPlugins.OllamaResolver(registry));
            context.RegisterPostDownloadAction(new OllamaPlugins.AddToOllamaAction(registry));
        }
    }

    private sealed class BlobRegistry(string blobUrl) : OllamaPlugins.IOllamaRegistry
    {
        private const string ManifestJson =
            "{ \"layers\": [ { \"mediaType\": \"application/vnd.ollama.image.model\", " +
            "\"digest\": \"sha256:x\", \"size\": 4096 } ] }";

        public Task<OllamaPlugins.OllamaManifest> GetManifestAsync(
            OllamaPlugins.OllamaModelRef model, CancellationToken ct) =>
            Task.FromResult(OllamaPlugins.OllamaManifest.Parse(ManifestJson));

        public string BlobUrl(OllamaPlugins.OllamaModelRef model, string digest) => blobUrl;

        public Task DownloadBlobAsync(OllamaPlugins.OllamaModelRef model, string digest,
            string destinationPath, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> GetTagsAsync(
            OllamaPlugins.OllamaModelRef model, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    /// <summary>Serves fixed bytes per path over loopback, with the Range support the engine expects.</summary>
    private sealed class TinyServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly Dictionary<string, byte[]> _files;
        public string Url { get; }

        public TinyServer(Dictionary<string, byte[]> files)
        {
            _files = files;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://127.0.0.1:{port}/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(Url);
            _listener.Start();
            _ = Task.Run(LoopAsync);
        }

        private async Task LoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { break; }
                _ = Task.Run(() => Handle(ctx));
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            try
            {
                var path = ctx.Request.Url?.AbsolutePath ?? "/";
                if (!_files.TryGetValue(path, out var body))
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                ctx.Response.Headers["Accept-Ranges"] = "bytes";
                var range = ctx.Request.Headers["Range"];
                var start = 0;
                var end = body.Length - 1;
                if (!string.IsNullOrEmpty(range) && range.StartsWith("bytes=", StringComparison.Ordinal))
                {
                    var span = range[6..].Split('-');
                    if (int.TryParse(span[0], out var s)) start = s;
                    if (span.Length > 1 && int.TryParse(span[1], out var e)) end = Math.Min(e, body.Length - 1);
                    ctx.Response.StatusCode = 206;
                    ctx.Response.Headers["Content-Range"] = $"bytes {start}-{end}/{body.Length}";
                }

                var length = end - start + 1;
                ctx.Response.ContentLength64 = length;
                if (ctx.Request.HttpMethod != "HEAD")
                    ctx.Response.OutputStream.Write(body, start, length);
                ctx.Response.Close();
            }
            catch
            {
                // a client that went away mid-response is not this test's concern
            }
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { /* already stopped */ }
            try { _listener.Close(); } catch { /* already closed */ }
        }
    }
}
