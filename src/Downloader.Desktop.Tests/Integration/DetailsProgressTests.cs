using System;
using System.IO;
using System.Linq;
using System.Net;
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
/// The details window's per-connection strip, driven by a REAL multipart download over loopback.
///
/// This is the part of the dialog that only exists once an engine is running, and it has a history of
/// showing the wrong thing: "7 of 8 connections" because the part set was seeded once and never
/// reconciled, segments left reading "Downloading" on a finished file, and an empty strip when the
/// dialog was opened before the manager had finished resolving redirects and assigned the engine
/// handle. All three need an actual engine with actual chunks, so a stub would not have caught them.
///
/// Loopback only — no external network.
/// </summary>
public class DetailsProgressTests
{
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_finished_download_shows_every_connection_complete()
    {
        var payload = new byte[512 * 1024];
        new Random(99).NextBytes(payload);
        using var server = new LoopbackServer(payload);

        var dir = Path.Combine(Path.GetTempPath(), "dldesktop_details_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var config = Config.New();
            config.Settings.ChunkCount = 4;
            config.Settings.ParallelDownload = true;
            config.Settings.MinimumSizeOfChunking = 1024;
            config.Settings.DefaultSavePath = dir;

            var manager = new DownloadManager();
            manager.Initialize(config);

            var item = manager.Add(new DownloadItem
            {
                Url = server.Url + "sample.bin",
                SaveFolder = dir,
            }, autoStart: true);

            // The dialog is opened immediately — before Start has finished resolving off-thread and
            // published the engine handle. It must attach when the handle arrives, not stay empty.
            var details = new DownloadDetailsViewModel(item);

            var finished = await PumpUntil(() => item.Status is DownloadStatus.Completed
                                                 or DownloadStatus.Failed
                                                 or DownloadStatus.Stopped, 60_000);

            Assert.True(finished, "the loopback download should finish well inside the timeout");
            Assert.Equal(DownloadStatus.Completed, item.Status);

            // Connections showed up despite the late attach…
            Assert.True(details.HasParts, "the segmented strip must populate from the engine's chunks");
            Assert.NotEmpty(details.Parts);
            Assert.False(string.IsNullOrWhiteSpace(details.PartsSummary));
            Assert.True(details.HasConfig);
            Assert.True(details.Connections > 0);

            // …and every one of them reads as finished. A segment left short is the "not full at 100%"
            // bug; one left saying "Downloading" is the stale-state bug.
            Assert.All(details.Parts, p => Assert.Equal(100, p.Progress, 0));

            // Each fragment keeps its own stable colour so the strip is readable.
            Assert.All(details.Parts, p => Assert.NotNull(p.Brush));

            details.Cleanup();
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task Opening_the_dialog_after_the_download_finished_still_shows_it_complete()
    {
        var payload = new byte[256 * 1024];
        new Random(7).NextBytes(payload);
        using var server = new LoopbackServer(payload);

        var dir = Path.Combine(Path.GetTempPath(), "dldesktop_details2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var config = Config.New();
            config.Settings.ChunkCount = 4;
            config.Settings.MinimumSizeOfChunking = 1024;
            config.Settings.DefaultSavePath = dir;

            var manager = new DownloadManager();
            manager.Initialize(config);

            var item = manager.Add(new DownloadItem { Url = server.Url + "sample.bin", SaveFolder = dir },
                autoStart: true);

            var finished = await PumpUntil(() => item.Status == DownloadStatus.Completed, 60_000);
            Assert.True(finished);

            // Constructed only now: the ctor has to reflect the terminal state on every segment
            // rather than leaving them mid-flight.
            var details = new DownloadDetailsViewModel(item);

            Assert.All(details.Parts, p => Assert.Equal(100, p.Progress, 0));
            Assert.Equal(100, item.Progress);
            details.Cleanup();
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Runs the dispatcher until <paramref name="condition"/> holds. The test thread IS the UI
    /// thread under the headless runtime, so the engine's posted callbacks only run while jobs are
    /// pumped — a plain await would deadlock.
    /// </summary>
    private static async Task<bool> PumpUntil(Func<bool> condition, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
                return true;
            await Task.Delay(25);
        }
        Dispatcher.UIThread.RunJobs();
        return condition();
    }

    /// <summary>Minimal loopback HTTP server serving one payload, with the Range support the engine
    /// needs to split the file across connections.</summary>
    private sealed class LoopbackServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly byte[] _data;

        public LoopbackServer(byte[] data)
        {
            _data = data;
            var port = FreePort();
            Url = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
            new Thread(Loop) { IsBackground = true }.Start();
        }

        public string Url { get; }

        private static int FreePort()
        {
            var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        private void Loop()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch { return; }

                try
                {
                    ctx.Response.Headers["Accept-Ranges"] = "bytes";
                    var range = ctx.Request.Headers["Range"];
                    var from = 0;
                    var to = _data.Length - 1;

                    if (!string.IsNullOrEmpty(range) && range.StartsWith("bytes="))
                    {
                        var parts = range["bytes=".Length..].Split('-');
                        if (parts.Length == 2)
                        {
                            if (int.TryParse(parts[0], out var f)) from = f;
                            if (int.TryParse(parts[1], out var t)) to = Math.Min(t, _data.Length - 1);
                        }
                        ctx.Response.StatusCode = 206;
                        ctx.Response.Headers["Content-Range"] = $"bytes {from}-{to}/{_data.Length}";
                    }

                    if (ctx.Request.HttpMethod == "HEAD")
                    {
                        ctx.Response.ContentLength64 = _data.Length;
                        ctx.Response.Close();
                        continue;
                    }

                    var length = to - from + 1;
                    ctx.Response.ContentLength64 = length;
                    ctx.Response.OutputStream.Write(_data, from, length);
                    ctx.Response.Close();
                }
                catch
                {
                    // client went away mid-write
                }
            }
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }
        }
    }
}
