using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// A finished download is judged by its file — but not before that file exists.
/// <para>
/// The engine raises its completion BEFORE moving the finished file into place, so the check for "the
/// engine says success but produced no file" (issue #9) was reading the save folder during the gap and
/// failing perfectly good downloads with "nothing was downloaded" — visible on a loaded machine and on
/// CI, invisible on a fast idle one. The file now gets a grace period to appear, and only a folder that
/// STAYS empty fails the row.
/// </para>
/// </summary>
public class LateFileCompletionTests
{
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_download_whose_file_appears_a_moment_late_still_completes()
    {
        Localizer.Instance.Load("en");
        var folder = Path.Combine(Path.GetTempPath(), "dldesktop-late-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var saved = Path.Combine(folder, "late.bin");

        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        var vm = manager.Add(new DownloadItem
        {
            Url = "http://127.0.0.1:9/late.bin", // nothing listening; the engine's own outcome is not the point
            SaveFolder = folder,
            FileName = "late.bin",
        }, autoStart: false);

        // The engine's view: finished, with the file not there yet. Then it lands, as the engine's own
        // final move would make it.
        var original = DownloadManager.EmptyFileGrace;
        DownloadManager.EmptyFileGrace = TimeSpan.FromSeconds(5);
        try
        {
            Assert.True(DownloadManager.LooksEmptyAfterCompletion(saved),
                "the file must be absent to begin with, or this test proves nothing");

            await Task.Delay(150);
            await File.WriteAllBytesAsync(saved, new byte[1024], TestContext.Current.CancellationToken);

            // A file that exists and has bytes is never "nothing was downloaded", whenever it showed up.
            Assert.False(DownloadManager.LooksEmptyAfterCompletion(saved));
        }
        finally
        {
            DownloadManager.EmptyFileGrace = original;
            try { Directory.Delete(folder, true); } catch { /* best-effort */ }
            Dispatcher.UIThread.RunJobs();
            GC.KeepAlive(vm);
        }
    }

    /// <summary>A download that really produced nothing must still fail — the grace period is a delay,
    /// not an amnesty. Driven end to end through a server that answers the size probe and refuses every
    /// body, which is the shape that made a row read "finished" over an empty folder.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_download_that_produced_nothing_still_fails_after_the_grace_period()
    {
        Localizer.Instance.Load("en");
        var original = DownloadManager.EmptyFileGrace;
        DownloadManager.EmptyFileGrace = TimeSpan.FromMilliseconds(300); // process-wide; restored below
        var folder = Path.Combine(Path.GetTempPath(), "dldesktop-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            using var server = new EmptyBodyServer();
            var manager = new DownloadManager();
            manager.Initialize(Config.New());
            manager.Add(new DownloadItem
            {
                Url = server.Url + "file.bin",
                SaveFolder = folder,
                FileName = "file.bin",
            }, autoStart: true);
            var vm = manager.Items[0];

            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (vm.Status is not (global::Downloader.DownloadStatus.Failed
                                     or global::Downloader.DownloadStatus.Completed)
                   && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(25);
            }
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(global::Downloader.DownloadStatus.Failed, vm.Status);
            Assert.False(string.IsNullOrWhiteSpace(vm.ErrorMessage));
            // No FINISHED file — the engine's own .download scratch file may well be sitting there.
            Assert.False(File.Exists(Path.Combine(folder, "file.bin")));
        }
        finally
        {
            DownloadManager.EmptyFileGrace = original;
            try { Directory.Delete(folder, true); } catch { /* best-effort */ }
        }
    }

    /// <summary>Answers the engine's size probe and then refuses every body.</summary>
    private sealed class EmptyBodyServer : IDisposable
    {
        private readonly HttpListener _listener;
        public string Url { get; }

        public EmptyBodyServer()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://127.0.0.1:{port}/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(Url);
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync(); } catch { break; }
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            var range = ctx.Request.Headers["Range"];
                            if (range == "bytes=0-0") // the size probe: answer it, so the download starts
                            {
                                ctx.Response.StatusCode = 206;
                                ctx.Response.Headers["Accept-Ranges"] = "bytes";
                                ctx.Response.Headers["Content-Range"] = "bytes 0-0/4096";
                                ctx.Response.ContentLength64 = 1;
                                ctx.Response.OutputStream.WriteByte(0);
                            }
                            else
                            {
                                ctx.Response.StatusCode = 403; // and never serve a byte of it
                            }
                            ctx.Response.Close();
                        }
                        catch { /* the client went away */ }
                    });
                }
            });
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { /* already stopped */ }
            try { _listener.Close(); } catch { /* already closed */ }
        }
    }
}
