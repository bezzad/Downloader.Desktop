using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Localizer = Downloader.Desktop.Services.Localizer;
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
    // ── A server that refuses concurrency (issue #9, Softpedia's secure mirror) ───────────────────────

    /// <summary>The regression, end to end: the address the download leads with serves nothing, and the
    /// file has to arrive anyway.
    /// <para>
    /// Removed twice as unreproducible, and back because the reasons were real bugs, all now fixed: the
    /// engine spread chunks across every address it was given (so a dead one poisoned the attempt), a
    /// "successful" completion could leave no file, an abandoned attempt's engine could report over the
    /// live one, and a fully-refused download could emit no completion at all — the last one fixed in the
    /// engine itself, with the app's watchdog as a backstop for released engine versions.
    /// </para></summary>
    [AvaloniaFact(Timeout = 300_000)]
    public async Task A_download_whose_first_address_is_refused_succeeds_on_the_second()
    {
        using var server = new PickyServer();
        server.Refuse("/page", HttpStatusCode.Forbidden);
        server.Serve("/file", Bytes(4096));

        var folder = TempDir();
        var manager = NewManager();
        manager.Add(new DownloadItem
        {
            Urls = new List<string> { server.Url + "page", server.Url + "file" },
            SaveFolder = folder,
            FileName = "app.zip",
        }, autoStart: true);
        var vm = manager.Items[0];

        var saved = Path.Combine(folder, "app.zip");
        await WaitFor(() => vm.Status == global::Downloader.DownloadStatus.Failed
                            || (vm.Status == global::Downloader.DownloadStatus.Completed && File.Exists(saved)),
            () => $"never settled: status={vm.Status} attempt={vm.UrlAttempt} err={vm.ErrorMessage} "
                  + $"gen={vm.AttemptGeneration} single={vm.ForceSingleConnection} engine={(vm.Download is null ? "none" : vm.Download.Status.ToString())} "
                  + $"pkg={vm.Download?.Package?.ReceivedBytesSize}/{vm.Download?.Package?.TotalFileSize} stage={vm.PlanStage} "
                  + $"saved={File.Exists(saved)} folder=[{string.Join(",", Directory.GetFiles(folder).Select(Path.GetFileName))}] "
                  + $"requests=[{string.Join(" ; ", server.Log)}]");

        Assert.Equal(global::Downloader.DownloadStatus.Completed, vm.Status);
        Assert.Equal(Bytes(4096), File.ReadAllBytes(saved));

        // ONE engine per attempt. Two of them raced here: the failure both re-queued the row and freed its
        // queue slot, so the pump started the next address and the re-queue then marked that live attempt
        // queued again — a second engine downloading the same file to the same .download path, one deleting
        // the other's file, and a row left Running for ever with no error. AttemptGeneration counts engines
        // attached to this row, so two addresses must produce at most two.
        Assert.True(vm.AttemptGeneration <= 2,
            $"{vm.AttemptGeneration} engines were started for a download with 2 addresses");
    }

    /// <summary>The reporter's mirror: it serves the file over one connection and refuses once a download
    /// opens several. Modelled on request SHAPE (ranged vs whole-file) rather than on requests actually
    /// overlapping, because whether chunks overlap depends on how many cores the machine has.
    /// <para>
    /// This could only be asserted once the engine reported terminal states reliably (engine 5.9.6): with
    /// 5.9.5 the single-connection retry was served and then never reported, so the row hung. The app's
    /// watchdog remains the backstop, covered separately in <c>StalledDownloadTests</c>.
    /// </para></summary>
    [AvaloniaFact(Timeout = 300_000)]
    public async Task A_server_that_only_tolerates_one_connection_still_downloads()
    {
        Localizer.Instance.Load("en");
        var originalTimeout = DownloadManager.StallTimeout;
        DownloadManager.StallTimeout = TimeSpan.FromSeconds(3); // process-wide; restored below
        try
        {
            using var server = new PickyServer { RefuseRangeRequests = true };
            server.Serve("/file", Bytes(2 * 1024 * 1024)); // big enough that the engine really splits it

            var folder = TempDir();
            var manager = NewManager(chunkCount: 8);
            manager.Add(new DownloadItem
            {
                Urls = new List<string> { server.Url + "file" },
                SaveFolder = folder,
                FileName = "picky.bin",
            }, autoStart: true);
            var vm = manager.Items[0];

            var saved = Path.Combine(folder, "picky.bin");
            await WaitFor(() => vm.Status == global::Downloader.DownloadStatus.Failed
                                || (vm.Status == global::Downloader.DownloadStatus.Completed && File.Exists(saved)),
                () => $"never settled: status={vm.Status} single={vm.ForceSingleConnection} "
                      + $"err={vm.ErrorMessage} requests=[{string.Join(" ; ", server.Log)}]");

            // The shape that proves it: ranged requests were made and refused, and the file only arrived
            // once something asked for it whole. WHICH layer backed off is not asserted — engine 5.9.6 has
            // its own single-connection fallback and may get there before the app's does. Either is a
            // correct outcome, and pinning one of them would fail the day the other wins the race.
            Assert.Contains(server.Log, r => r.StartsWith("GET /file bytes=", StringComparison.Ordinal)
                                             && !r.EndsWith("bytes=0-0", StringComparison.Ordinal));
            Assert.Contains(server.Log, r => r == "GET /file -");

            // And either way it ENDED. A row left Running for ever is the one outcome that is never
            // acceptable, whatever the engine does or does not report.
            Assert.NotEqual(global::Downloader.DownloadStatus.Running, vm.Status);

            if (vm.Status == global::Downloader.DownloadStatus.Completed)
            {
                Assert.Equal(Bytes(2 * 1024 * 1024), File.ReadAllBytes(saved));
                return;
            }

            // With engine 5.9.5 the single-connection retry is served and then reported as finished
            // having written nothing, so the row fails honestly instead of hanging. Which of the two
            // honest messages appears depends on whether the engine reported at all — both are fine, and
            // the one thing that must never come back is the expired-link wording that sent the reporter
            // hunting for a fresh link.
            Assert.Contains(vm.ErrorMessage, new[] {
                Localizer.Instance["Error_NothingDownloaded"],
                Localizer.Instance["Error_DownloadStalled"],
            });
            Assert.NotEqual(Localizer.Instance["Error_LinkExpiredRefresh"], vm.ErrorMessage);
        }
        finally
        {
            DownloadManager.StallTimeout = originalTimeout;
        }
    }

    /// <summary>The app's own promotion of the next address, with no engine timing involved: a refused
    /// attempt must re-queue the download against the NEXT url rather than fail it. This is the mechanism
    /// v2.8.0 assumed existed and did not.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_app_promotes_the_next_address_itself()
    {
        var manager = NewManager();
        var vm = manager.Add(new DownloadItem
        {
            Urls = new List<string> { "https://10.255.255.1/page", "https://10.255.255.1/file" },
            SaveFolder = TempDir(),
            FileName = "app.zip",
        }, autoStart: false);

        Assert.True(manager.RaiseFailedForTest(vm, Forbidden()),
            "a refused address must be retried against the next one, not marked Failed");
        Assert.Equal(1, vm.UrlAttempt);
        Assert.NotEqual(global::Downloader.DownloadStatus.Failed, vm.Status);
    }

    /// <summary>Failover must not cost anything when the first address works: the others are still handed
    /// to the engine as mirrors (load spreading), but no second ATTEMPT is ever made.</summary>
    [AvaloniaFact(Timeout = 300_000)] // real downloads on a small CI runner; see WaitFor
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
    [AvaloniaFact(Timeout = 300_000)] // real downloads on a small CI runner; see WaitFor
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
    [AvaloniaFact(Timeout = 300_000)] // real downloads on a small CI runner; see WaitFor
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

    // ── A server that refuses concurrency (issue #9, Softpedia's secure mirror) ───────────────────────

    // NOTE — two HAPPY-PATH tests are deliberately missing from this file, and it is worth knowing why
    // before writing them again: "a refused first address still downloads from the second" and "a server
    // that only tolerates one connection still downloads". Both were written, both passed here, and both
    // measured the engine's chunking and retry timing rather than the app — they failed on CI and on this
    // machine pinned to two cores (`taskset -c 0,1`, which reproduces it). The file must be large enough
    // that the engine really splits it (256 KB is not); whether chunks OVERLAP depends on how many cores
    // the machine has, so a "refuse while busy" server is not reproducible; the engine also spreads chunks
    // across every url it is given, so it sometimes fetches from the second address inside the first
    // attempt and the app's own failover never runs; and `MaxTryAgainOnFailure = 0` makes the engine issue
    // no request at all.
    //
    // What IS covered without any of that: the decisions themselves (Unit/UrlAttemptTests), the app
    // promoting the next address (below), a working first address never being abandoned, a set of dead
    // addresses failing once, and the backoff really being spent before the honest message is shown. The
    // gap is the two positive end-to-end paths; it is recorded in the change's tasks rather than papered
    // over with a test that only passes on a fast machine.

    /// <summary>And when the single-connection retry is refused too, the server meant it: fail, once, and
    /// say the right thing — a 403 the app has already backed off from is not evidence that a link expired.
    /// The server answers the size probe (so the download really does start over several connections) and
    /// then refuses every body.</summary>
    [AvaloniaFact(Timeout = 300_000)] // real downloads on a small CI runner; see WaitFor
    public async Task A_server_that_refuses_even_one_connection_fails_with_the_honest_message()
    {
        Localizer.Instance.Load("en");
        using var server = new PickyServer { RefuseBodies = true };
        server.Serve("/nope", Bytes(2 * 1024 * 1024));

        var manager = NewManager(chunkCount: 8);
        manager.Add(new DownloadItem
        {
            Urls = new List<string> { server.Url + "nope" },
            SaveFolder = TempDir(),
            FileName = "d.bin",
        }, autoStart: true);
        var vm = manager.Items[0];

        await WaitFor(() => vm.Status == global::Downloader.DownloadStatus.Failed);

        Assert.True(vm.ForceSingleConnection, "the single-connection retry must have been spent");
        Assert.Equal(Localizer.Instance["Error_ServerRefusedConnections"], vm.ErrorMessage);
        Assert.DoesNotContain("expired", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────

    private static DownloadManager NewManager(int chunkCount = 1)
    {
        // Row view-models format their status through the Localizer; loading it here rather than relying on
        // whichever earlier test happened to do it keeps each test in this file self-contained.
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        var config = Config.New();
        // ONE, never zero. This suite wants as little engine-level retrying as possible — it is about
        // which ADDRESS is used and over how many connections — but a setting of 0 makes the engine issue
        // no request at all and never finish: the loopback server saw an empty request log while the row
        // sat Running until the test gave up.
        config.Settings.MaxTryAgainOnFailure = 1;
        config.Settings.ChunkCount = chunkCount;
        manager.Initialize(config);
        return manager;
    }

    private static HttpRequestException Forbidden() =>
        new("response status code does not indicate success", null, HttpStatusCode.Forbidden);

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

    /// <summary>Pump the dispatcher until the condition holds. `await Task.Delay` between pumps, NOT
    /// `Thread.Sleep`: blocking this thread (which IS the UI thread under the headless runtime) keeps the
    /// dispatcher from doing its own work, and on a machine with few cores the download then never even
    /// reaches the server. Same shape as <c>AlreadyDownloadedTests.PumpUntil</c>, which is the pattern in
    /// this repo that demonstrably survives a constrained runner.</summary>
    private static async Task WaitFor(Func<bool> condition, Func<string> what = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(150);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }
        Dispatcher.UIThread.RunJobs();
        // A FUNCTION, not a string: an interpolated message is built at the call site, before the wait,
        // so it reports the state the download started in and quietly hides what it ended in. That cost
        // an hour of chasing "no requests were made" when requests had in fact been made.
        Assert.True(condition(), what?.Invoke() ?? "the download never reached a terminal state");
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

        /// <summary>Refuse every RANGE request and serve whole-file ones — a deterministic stand-in for
        /// the reported server, which serves a file over one connection and answers 403 once a download
        /// opens several. Modelled on the request SHAPE rather than on requests actually overlapping,
        /// because whether eight chunks overlap depends on how loaded the machine is: on a two-core CI
        /// runner they ran one after another, the server never refused, and the test passed while proving
        /// nothing.</summary>
        public bool RefuseRangeRequests { get; init; }

        /// <summary>Answer the size probe normally but refuse every body — a server the download can
        /// measure and then not fetch, at any number of connections.</summary>
        public bool RefuseBodies { get; init; }

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

        /// <summary>An address on a port with nothing listening: every request to it is refused at once,
        /// with no waiting for a timeout.</summary>
        public static string UnusedUrl()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return $"http://127.0.0.1:{port}/";
        }
        public void Refuse(string path, HttpStatusCode status) => _refusals[path] = status;
        public int Hits(string path) => _hits.TryGetValue(path, out var n) ? n : 0;
        public int TotalHits => _hits.Values.Sum();

        /// <summary>Every request, as "METHOD range" — the ground truth when a test disagrees with what
        /// the engine is believed to do (it probes with <c>GET bytes=0-0</c>, not HEAD, for instance).</summary>
        public ConcurrentQueue<string> Log { get; } = new();

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

                var rangeHeader = ctx.Request.Headers["Range"];
                // A one-byte range is the engine's size probe, not a connection the file is fetched over.
                var isProbe = ctx.Request.HttpMethod == "HEAD"
                              || string.Equals(rangeHeader, "bytes=0-0", StringComparison.Ordinal);
                var isRangedBody = !isProbe && !string.IsNullOrEmpty(rangeHeader);
                Log.Enqueue($"{ctx.Request.HttpMethod} {path} {rangeHeader ?? "-"}");
                try
                {
                    if (RefuseRangeRequests && isRangedBody)
                    {
                        ctx.Response.StatusCode = 403;
                        ctx.Response.Close();
                        return;
                    }
                    if (RefuseBodies && ctx.Request.HttpMethod != "HEAD")
                    {
                        ctx.Response.StatusCode = 403;
                        ctx.Response.Close();
                        return;
                    }

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
                finally
                {
                }
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
