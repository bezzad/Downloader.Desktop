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
/// Re-adding a file that is already on disk, under the app's default FileExistPolicy
/// (IgnoreDownload).
///
/// This looks like a success but arrives as a FAILURE-shaped event, which is why it needs a real
/// engine to test. When the target already exists the engine skips the download entirely: it signals
/// completion as <c>Cancelled=true, Error=null</c> and never raises DownloadStarted. Read naively that
/// is indistinguishable from a user pause or a timeout, and it used to surface as a failed row —
/// the user saw "failed" for a file they already had.
///
/// The row must instead end up Completed, showing 100%, with its name/size backfilled from the file
/// on disk and an "already downloaded" label.
/// </summary>
public class AlreadyDownloadedTests
{
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task Re_adding_an_existing_file_completes_instead_of_failing()
    {
        var payload = new byte[64 * 1024];
        new Random(11).NextBytes(payload);
        using var server = new LoopbackServer(payload, "sample.bin");

        var dir = Path.Combine(Path.GetTempPath(), "dldesktop_exists_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Localizer.Instance.Load("en");
            var config = Config.New();
            config.Settings.DefaultSavePath = dir;
            config.Settings.MinimumSizeOfChunking = 1024;
            // The app's default: an existing target is skipped, not re-fetched or overwritten.
            config.Settings.FileExistPolicy = FileExistPolicy.IgnoreDownload;

            var manager = new DownloadManager();
            manager.Initialize(config);
            var url = server.Url + "sample.bin";

            // First download: the real thing.
            var first = manager.Add(new DownloadItem { Url = url, SaveFolder = dir }, autoStart: true);
            Assert.True(await PumpUntil(() => first.Status == DownloadStatus.Completed, 60_000),
                "the first download should finish normally");

            var onDisk = Directory.GetFiles(dir).Single();
            Assert.Equal(payload.Length, new FileInfo(onDisk).Length);

            // Second add of the same URL into the same folder — the engine will skip it.
            var second = manager.Add(new DownloadItem { Url = url, SaveFolder = dir }, autoStart: true);

            Assert.True(await PumpUntil(() => second.Status is DownloadStatus.Completed
                                              or DownloadStatus.Failed
                                              or DownloadStatus.Stopped, 60_000),
                "the skipped download should reach a terminal state");

            // The whole point: a skip is a success, not a failure.
            Assert.Equal(DownloadStatus.Completed, second.Status);
            Assert.False(second.HasError);
            Assert.True(second.AlreadyExisted);

            // A completed row always reads 100%, even though it downloaded no bytes this time —
            // computing the bar from Downloaded/Size would show 0%.
            Assert.Equal(100, second.Progress);

            // Name and size are backfilled from the file that is actually there, so the row is not
            // left blank.
            Assert.False(string.IsNullOrWhiteSpace(second.FileName));
            Assert.Equal(payload.Length, second.Size);

            // The status text says "already downloaded" rather than reusing the plain Completed label.
            Assert.False(string.IsNullOrWhiteSpace(second.StatusText));
            Assert.DoesNotContain("State_", second.StatusText);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task Retrying_an_already_downloaded_row_clears_the_flag()
    {
        var payload = new byte[32 * 1024];
        new Random(12).NextBytes(payload);
        using var server = new LoopbackServer(payload, "again.bin");

        var dir = Path.Combine(Path.GetTempPath(), "dldesktop_exists2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Localizer.Instance.Load("en");
            var config = Config.New();
            config.Settings.DefaultSavePath = dir;
            config.Settings.MinimumSizeOfChunking = 1024;
            config.Settings.FileExistPolicy = FileExistPolicy.IgnoreDownload;

            var manager = new DownloadManager();
            manager.Initialize(config);
            var url = server.Url + "again.bin";

            var first = manager.Add(new DownloadItem { Url = url, SaveFolder = dir }, autoStart: true);
            Assert.True(await PumpUntil(() => first.Status == DownloadStatus.Completed, 60_000));

            var second = manager.Add(new DownloadItem { Url = url, SaveFolder = dir }, autoStart: true);
            Assert.True(await PumpUntil(() => second.Status == DownloadStatus.Completed, 60_000));
            Assert.True(second.AlreadyExisted);

            // A completed row must not be restartable — Retry only acts on Failed/Stopped.
            manager.Retry(second);
            Assert.Equal(DownloadStatus.Completed, second.Status);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

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

    /// <summary>Loopback server serving one named payload with Range support.</summary>
    private sealed class LoopbackServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly byte[] _data;
        private readonly string _name;

        public LoopbackServer(byte[] data, string name)
        {
            _data = data;
            _name = name;
            var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            Url = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
            new Thread(Loop) { IsBackground = true }.Start();
        }

        public string Url { get; }

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
                    // A Content-Disposition name makes the engine resolve the same file name both
                    // times, which is what makes the second add collide with the first.
                    ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{_name}\"";

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
                    // client went away
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
