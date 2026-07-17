using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// The reported leak (task #11): a completed download's <c>DownloadService</c> (with its package + buffers)
/// was never released, so thousands of finished rows accumulated GBs that only a restart cleared. These
/// tests download many small files through the real manager/engine over a loopback server and assert the
/// engine handle is released once a row reaches a terminal state — and that a released row can still be
/// retried (a fresh engine is built).
/// </summary>
public class MemoryReleaseTests
{
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task Completed_downloads_release_their_engine()
    {
        var payload = new byte[16 * 1024];
        new Random(7).NextBytes(payload);
        using var server = new Loopback(payload);

        var dir = Path.Combine(Path.GetTempPath(), "dldesktop_mem_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var cfg = Config.New();
            cfg.Settings.DefaultSavePath = dir;
            cfg.Settings.EnableNotifications = false;
            cfg.Settings.ChunkCount = 2;
            cfg.DefaultQueue.MaxConcurrent = 4;

            var manager = new DownloadManager();
            manager.Initialize(cfg);

            const int count = 30;
            var vms = new List<DownloadItemViewModel>();
            for (var i = 0; i < count; i++)
                vms.Add(manager.Add(new DownloadItem { Urls = new() { server.Url + $"file{i}.bin" }, SaveFolder = dir }, autoStart: true));

            await PumpUntil(() => vms.All(v => v.Status == DownloadStatus.Completed), (int)TestTimeouts.SlowMs - 5000);

            Assert.All(vms, v => Assert.Equal(DownloadStatus.Completed, v.Status));
            // The core assertion: no completed row still holds a live engine handle (or its package).
            Assert.All(vms, v => Assert.Null(v.Download));

            // Display state the grid/resume rely on must survive the release.
            Assert.All(vms, v =>
            {
                Assert.False(string.IsNullOrEmpty(v.FileName));
                Assert.Equal(100, (int)v.Progress);
            });
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_released_stopped_row_can_be_retried_to_completion()
    {
        var payload = new byte[64 * 1024];
        new Random(11).NextBytes(payload);
        using var server = new Loopback(payload);

        var dir = Path.Combine(Path.GetTempPath(), "dldesktop_mem_retry_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var cfg = Config.New();
            cfg.Settings.DefaultSavePath = dir;
            cfg.Settings.EnableNotifications = false;

            var manager = new DownloadManager();
            manager.Initialize(cfg);

            var vm = manager.Add(new DownloadItem { Urls = new() { server.Url + "retry.bin" }, SaveFolder = dir }, autoStart: true);

            // Stop it → terminal (Stopped) → engine released.
            manager.Cancel(vm);
            await PumpUntil(() => vm.Status == DownloadStatus.Stopped && vm.Download == null, 10000);
            Assert.Equal(DownloadStatus.Stopped, vm.Status);
            Assert.Null(vm.Download); // released on stop

            // Retry must rebuild a fresh engine and complete.
            manager.Retry(vm);
            await PumpUntil(() => vm.Status == DownloadStatus.Completed, 20000);
            Assert.Equal(DownloadStatus.Completed, vm.Status);
            Assert.Null(vm.Download); // released again after completing
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static async Task PumpUntil(Func<bool> condition, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Minimal loopback HTTP server with Range support (same shape as IntegrationTests).</summary>
    private sealed class Loopback : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly byte[] _data;
        public string Url { get; }

        public Loopback(byte[] data)
        {
            _data = data;
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
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
                var resp = ctx.Response;
                resp.Headers["Accept-Ranges"] = "bytes";
                if (ctx.Request.HttpMethod == "HEAD")
                {
                    resp.ContentLength64 = _data.Length;
                    resp.OutputStream.Close();
                    return;
                }

                var range = ctx.Request.Headers["Range"];
                int start = 0, end = _data.Length - 1;
                if (!string.IsNullOrEmpty(range) && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = range.Substring(6).Split('-');
                    if (parts.Length == 2)
                    {
                        if (int.TryParse(parts[0], out var s)) start = s;
                        if (int.TryParse(parts[1], out var e)) end = e;
                    }
                    end = Math.Min(end, _data.Length - 1);
                    start = Math.Max(0, Math.Min(start, end));
                    resp.StatusCode = 206;
                    resp.AddHeader("Content-Range", $"bytes {start}-{end}/{_data.Length}");
                }

                var len = end - start + 1;
                resp.ContentLength64 = len;
                resp.OutputStream.Write(_data, start, len);
                resp.OutputStream.Close();
            }
            catch
            {
                try { ctx.Response.Abort(); } catch { /* ignore */ }
            }
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { /* ignore */ }
            try { _listener.Close(); } catch { /* ignore */ }
        }
    }
}
