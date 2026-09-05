using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// Releasing a finished row's engine must never BLOCK the thread that releases it.
///
/// The engine's synchronous <c>Dispose()</c> is <c>Clear().Wait()</c>, and <c>Clear()</c> waits on the
/// semaphore the running <c>StartDownload</c> holds until it returns. Nearly every caller of the release
/// path is reached from that operation's own completion event, through <c>OnUi</c> — so disposing inline
/// waits for the operation that is waiting for the caller, on the UI thread. Diagnosed from a hang dump:
///
///   Task.Wait() ← AbstractDownloadService.Dispose() ← ReleaseEngine ← FinishTerminal ← OnUi
///               ← the engine's DownloadFileCompleted ← SendDownloadCompletionSignal ← StartDownload
///
/// In the app that freezes the window. Under the headless runtime the UI thread IS the test thread, so
/// the whole suite stops and CI aborts on the 3-minute inactivity timer, blaming whichever test came
/// next — the intermittent "test host hung" that had been misread as infrastructure flakiness.
/// </summary>
public class EngineReleaseBlockingTests
{
    /// <summary>Serves a response that never finishes, so the engine stays busy (and keeps its semaphore)
    /// for as long as the test needs.</summary>
    private sealed class StallingServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        public string Url { get; }

        public StallingServer()
        {
            var port = FreePort();
            Url = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
                    catch { return; }

                    // Announce a big file, then dribble bytes forever: the download starts (so the engine
                    // is genuinely busy) and never completes on its own.
                    try
                    {
                        ctx.Response.StatusCode = 200;
                        ctx.Response.ContentLength64 = 64 * 1024 * 1024;
                        ctx.Response.Headers["Accept-Ranges"] = "bytes";
                        var chunk = new byte[1024];
                        while (!_cts.IsCancellationRequested)
                        {
                            await ctx.Response.OutputStream.WriteAsync(chunk, _cts.Token).ConfigureAwait(false);
                            await ctx.Response.OutputStream.FlushAsync(_cts.Token).ConfigureAwait(false);
                            await Task.Delay(50, _cts.Token).ConfigureAwait(false);
                        }
                    }
                    catch { /* client went away or the test ended */ }
                }
            });
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { /* best-effort */ }
            try { _listener.Close(); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// The real shape of the bug: the release happens ON THE UI THREAD, FROM INSIDE the engine's own
    /// completion event — which is exactly what the app does (its handler marshals through <c>OnUi</c>).
    /// At that moment <c>StartDownload</c> still holds its semaphore and cannot release it until this
    /// handler returns, so a blocking <c>Dispose()</c> waits for the thing that is waiting for it.
    ///
    /// This test hangs on the old code and passes on the fixed code — a plain "does it return quickly"
    /// check does NOT catch it, because <c>Clear()</c> cancels first and the semaphore frees when the
    /// release is not nested inside the callback (the sibling test below passes either way; it guards
    /// the weaker "don't block" property, not this one).
    ///
    /// Verified by reverting the fix: the run had to be killed by an outer 400s timeout, and note it
    /// blew straight past this test's own 180s <c>Timeout</c> — xunit cannot time out a test whose
    /// dispatcher is the thing that is deadlocked, which is exactly why CI reported "test host hung"
    /// against an unrelated test instead of failing here. With the fix it passes in under half a second.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task Releasing_from_inside_the_engines_own_completion_does_not_deadlock()
    {
        var payload = new byte[32 * 1024];
        new Random(11).NextBytes(payload);
        using var server = new SmallFileServer(payload);
        var dir = Path.Combine(Path.GetTempPath(), "dldesktop_deadlock_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var cfg = Config.New();
            cfg.Settings.DefaultSavePath = dir;
            cfg.Settings.EnableNotifications = false;
            cfg.Settings.ChunkCount = 1;

            var manager = new DownloadManager();
            manager.Initialize(cfg);

            var vm = manager.Add(new DownloadItem { Urls = { server.Url + "small.bin" }, SaveFolder = dir },
                autoStart: true);

            var attached = DateTime.UtcNow.AddSeconds(20);
            while (vm.Download == null && DateTime.UtcNow < attached)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20);
            }
            Assert.NotNull(vm.Download);

            using var released = new ManualResetEventSlim(false);
            vm.Download.DownloadFileCompleted += (_, _) =>
            {
                // Still inside StartDownload, semaphore held. Hop to the UI thread the way the app's own
                // handler does and release from there — the exact stack the hang dump showed.
                try { Dispatcher.UIThread.Invoke(() => manager.RaiseCompletedForTest(vm)); }
                catch { /* a second completion after the row is gone is fine */ }
                released.Set();
            };

            // Pump the dispatcher so the UI-thread hop can run. On the old code the hop never returns:
            // it blocks inside RunJobs, this loop stops turning, and the test dies on its timeout.
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!released.IsSet && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20);
            }

            Assert.True(released.IsSet,
                "releasing the engine from inside its own completion event never returned — the engine " +
                "must be disposed off that call stack (see the class comment)");
            Assert.Null(vm.Download);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>Serves one small file, so the download actually completes and raises the event.</summary>
    private sealed class SmallFileServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        public string Url { get; }

        public SmallFileServer(byte[] payload)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
                    catch { return; }
                    try
                    {
                        ctx.Response.StatusCode = 200;
                        ctx.Response.ContentLength64 = payload.Length;
                        await ctx.Response.OutputStream.WriteAsync(payload, _cts.Token).ConfigureAwait(false);
                        ctx.Response.Close();
                    }
                    catch { /* client went away */ }
                }
            });
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { /* best-effort */ }
            try { _listener.Close(); } catch { /* best-effort */ }
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task Releasing_a_busy_engine_does_not_block_the_caller()
    {
        using var server = new StallingServer();
        var dir = Path.Combine(Path.GetTempPath(), "dldesktop_release_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var cfg = Config.New();
            cfg.Settings.DefaultSavePath = dir;
            cfg.Settings.EnableNotifications = false;
            cfg.Settings.ChunkCount = 2;

            var manager = new DownloadManager();
            manager.Initialize(cfg);

            var vm = manager.Add(new DownloadItem { Urls = { server.Url + "stalls.bin" }, SaveFolder = dir },
                autoStart: true);

            // Wait until the engine really exists and is transferring — that is when it holds the
            // semaphore a blocking Dispose() would wait on.
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (vm.Download is not { IsBusy: true } && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(25);
            }
            Assert.True(vm.Download is { IsBusy: true }, "the engine never started transferring");

            // The moment under test: the terminal bookkeeping (the same path the engine's own completion
            // event runs) releases the engine. It must hand control straight back.
            var clock = Stopwatch.StartNew();
            manager.RaiseCompletedForTest(vm);
            clock.Stop();

            Assert.Null(vm.Download); // the row let go of it
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3),
                $"releasing a busy engine blocked the caller for {clock.Elapsed.TotalSeconds:0.0}s — " +
                "it must be disposed off this call stack, or a completion arriving on the UI thread " +
                "deadlocks (see the class comment)");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
