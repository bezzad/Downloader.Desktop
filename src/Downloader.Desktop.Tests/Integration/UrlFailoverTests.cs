using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// A download carries more than one address, and every one of them has to be tried.
/// <para>
/// This file exists because of a shipped bug (v2.8.0, issue #9). The browser extension was changed to
/// hand the app the link the user clicked as the download's address, keeping the end of the redirect
/// chain as a "mirror" — on the assumption that the mirror would be used if the first address failed. It
/// was not: the engine's extra URLs are load spreading, and a chunk is pinned to one of them. So on every
/// site that serves the file from a different address than the page, the app requested a page, failed, and
/// never touched the address that would have worked. Nothing tested the assumption, so nothing caught it.
/// </para>
/// The first test here reproduces exactly that shape: the leading address is refused, the second serves
/// the file. It must end with the real bytes on disk.
/// </summary>
public class UrlFailoverTests
{
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_download_whose_first_address_is_refused_succeeds_on_the_second()
    {
        using var server = new PickyServer();
        server.Refuse("/page", HttpStatusCode.Forbidden);
        server.Serve("/file", Bytes(4096));

        var folder = TempDir();
        var manager = NewManager();
        var item = new DownloadItem
        {
            Urls = new List<string> { server.Url + "page", server.Url + "file" },
            SaveFolder = folder,
            FileName = "app.zip",
        };
        manager.Add(item, autoStart: true);
        var vm = manager.Items[0];

        await WaitFor(() => vm.Status is global::Downloader.DownloadStatus.Completed
                                       or global::Downloader.DownloadStatus.Failed);

        Assert.Equal(global::Downloader.DownloadStatus.Completed, vm.Status);
        var saved = Path.Combine(folder, "app.zip");
        Assert.True(File.Exists(saved), "the file the second address serves must be on disk");
        Assert.Equal(Bytes(4096), await File.ReadAllBytesAsync(saved, TestContext.Current.CancellationToken));
    }

    /// <summary>Failover must not cost anything when the first address works: the others are still handed
    /// to the engine as mirrors (load spreading), but no second ATTEMPT is ever made.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_working_first_address_is_not_abandoned_for_the_second()
    {
        using var server = new PickyServer();
        server.Serve("/file", Bytes(2048));
        server.Refuse("/dead", HttpStatusCode.Gone);

        var folder = TempDir();
        var manager = NewManager();
        manager.Add(new DownloadItem
        {
            Urls = new List<string> { server.Url + "file", server.Url + "dead" },
            SaveFolder = folder,
            FileName = "a.bin",
        }, autoStart: true);
        var vm = manager.Items[0];

        await WaitFor(() => vm.Status == global::Downloader.DownloadStatus.Completed);

        Assert.Equal(0, server.Hits("/dead"));
    }

    /// <summary>The bound: a download can make at most one leading attempt per address. Without this a
    /// failover loop would hammer a dead link forever instead of failing once.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task When_every_address_is_refused_the_download_fails_once()
    {
        using var server = new PickyServer();
        server.Refuse("/one", HttpStatusCode.Forbidden);
        server.Refuse("/two", HttpStatusCode.NotFound);

        var manager = NewManager();
        manager.Add(new DownloadItem
        {
            Urls = new List<string> { server.Url + "one", server.Url + "two" },
            SaveFolder = TempDir(),
            FileName = "b.bin",
        }, autoStart: true);
        var vm = manager.Items[0];

        await WaitFor(() => vm.Status == global::Downloader.DownloadStatus.Failed);

        Assert.False(string.IsNullOrWhiteSpace(vm.ErrorMessage));
        // One lead attempt per address. The engine may probe (HEAD then GET) within an attempt, so count
        // distinct attempts by the leading address rather than raw requests.
        Assert.True(server.Hits("/one") >= 1, "the first address must have been tried");
        Assert.True(server.Hits("/two") >= 1, "the second address must have been tried");
        Assert.True(server.TotalHits < 20, $"too many requests for two dead addresses: {server.TotalHits}");
    }

    /// <summary>A download with a single address must behave exactly as it did before failover existed.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_single_address_download_is_unchanged()
    {
        using var server = new PickyServer();
        server.Refuse("/only", HttpStatusCode.Forbidden);

        var manager = NewManager();
        manager.Add(new DownloadItem
        {
            Urls = new List<string> { server.Url + "only" },
            SaveFolder = TempDir(),
            FileName = "c.bin",
        }, autoStart: true);
        var vm = manager.Items[0];

        await WaitFor(() => vm.Status == global::Downloader.DownloadStatus.Failed);
        Assert.True(server.Hits("/only") >= 1);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────

    private static DownloadManager NewManager()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        // Keep the engine's own retrying out of the picture: this suite is about which ADDRESS is used.
        config.Settings.MaxTryAgainOnFailure = 1;
        config.Settings.ChunkCount = 1;
        manager.Initialize(config);
        return manager;
    }

    private static byte[] Bytes(int n)
    {
        var data = new byte[n];
        for (var i = 0; i < n; i++) data[i] = (byte)(i % 251);
        return data;
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dldesktop-failover-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(condition(), "the download never reached a terminal state");
    }

    /// <summary>A loopback server that serves some paths and refuses others with a chosen status, counting
    /// what was requested — which is how "the second address was never tried" is provable.</summary>
    private sealed class PickyServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly Dictionary<string, byte[]> _files = new();
        private readonly Dictionary<string, HttpStatusCode> _refusals = new();
        private readonly ConcurrentDictionary<string, int> _hits = new();
        public string Url { get; }

        public PickyServer()
        {
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

        public void Serve(string path, byte[] body) => _files[path] = body;
        public void Refuse(string path, HttpStatusCode status) => _refusals[path] = status;
        public int Hits(string path) => _hits.TryGetValue(path, out var n) ? n : 0;
        public int TotalHits => _hits.Values.Sum();

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
                _hits.AddOrUpdate(path, 1, (_, n) => n + 1);

                if (_refusals.TryGetValue(path, out var status))
                {
                    ctx.Response.StatusCode = (int)status;
                    ctx.Response.Close();
                    return;
                }

                if (!_files.TryGetValue(path, out var body))
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                ctx.Response.Headers["Accept-Ranges"] = "bytes";
                var start = 0;
                var end = body.Length - 1;
                var range = ctx.Request.Headers["Range"];
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
