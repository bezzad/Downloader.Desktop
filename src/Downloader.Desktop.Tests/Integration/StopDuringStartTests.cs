using System;
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
/// Stopping a download while its engine is still starting must leave it Stopped.
///
/// The engine's <c>DownloadStarted</c> is delivered to the row through <c>OnUi</c>, i.e. POSTED to the
/// dispatcher — so it can land after a Stop the user made while the engine was spinning up. That handler
/// set <c>Status = Running</c> unconditionally, which resurrected the row; the cancellation already in
/// flight then arrived while the row read Running, and the manager maps "cancelled while running" to
/// Failed (only a user pause/stop stays Paused/Stopped). So pressing Stop at the wrong moment reported a
/// failure for a download nothing had failed at.
///
/// This is the second cause of the intermittent CI red: it is what made
/// <c>MemoryReleaseTests.A_released_stopped_row_can_be_retried_to_completion</c> fail with
/// "Expected: Stopped, Actual: Failed" on a loaded runner, and only there — the window is wide enough to
/// hit only when the box is busy enough to delay the engine's start past the Stop.
/// </summary>
public class StopDuringStartTests
{
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void A_stop_during_startup_is_not_undone_by_the_engines_started_event()
    {
        using var server = new DribblingServer();
        var dir = Path.Combine(Path.GetTempPath(), "dldesktop_stopstart_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var cfg = Config.New();
            cfg.Settings.DefaultSavePath = dir;
            cfg.Settings.EnableNotifications = false;

            var manager = new DownloadManager();
            manager.Initialize(cfg);

            var vm = manager.Add(
                new DownloadItem { Urls = new() { server.Url + "slow.bin" }, SaveFolder = dir },
                autoStart: true);

            // Block the dispatcher rather than awaiting: posted callbacks then QUEUE instead of running,
            // which is exactly the ordering the bug needs — the engine gets going and raises its start
            // event while the row is still, from the dispatcher's point of view, untouched.
            Thread.Sleep(2500);

            // The user presses Stop. This runs on the dispatcher, so it completes before any queued
            // callback does.
            manager.Cancel(vm);
            Assert.Equal(DownloadStatus.Stopped, vm.Status);

            // Now let the queued engine callbacks in — including DownloadStarted.
            for (var i = 0; i < 5; i++)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(50);
            }

            // The stop must stand. Before the fix this read Running here, and Failed once the
            // cancellation completed.
            Assert.Equal(DownloadStatus.Stopped, vm.Status);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>Announces a large file and then dribbles bytes, so the download is genuinely under way
    /// but never finishes on its own during the test.</summary>
    private sealed class DribblingServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        public string Url { get; }

        public DribblingServer()
        {
            Url = $"http://127.0.0.1:{FreePort()}/";
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
                        ctx.Response.ContentLength64 = 64 * 1024 * 1024;
                        ctx.Response.Headers["Accept-Ranges"] = "bytes";
                        var buffer = new byte[1024];
                        while (!_cts.IsCancellationRequested)
                        {
                            await ctx.Response.OutputStream.WriteAsync(buffer, _cts.Token).ConfigureAwait(false);
                            await ctx.Response.OutputStream.FlushAsync(_cts.Token).ConfigureAwait(false);
                            await Task.Delay(50, _cts.Token).ConfigureAwait(false);
                        }
                    }
                    catch { /* the client went away, which is the point of the test */ }
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
            _cts.Cancel();
            try { _listener.Stop(); } catch (ObjectDisposedException) { }
            ((IDisposable)_listener).Dispose();
            _cts.Dispose();
        }
    }
}
