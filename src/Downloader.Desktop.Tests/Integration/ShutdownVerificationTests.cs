using System;
using System.Diagnostics;
using System.IO;
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
/// Real-app verification of the shutdown-on-completion flow: drives an actual loopback download
/// through a real <see cref="MainViewModel"/> + <see cref="DownloadManager"/> with the setting on, and
/// confirms that completing the download pops the real <see cref="Views.ShutdownView"/> countdown
/// dialog AND that Cancel stops it — WITHOUT ever powering the machine off (PowerOffOverride seam).
///
/// Gated behind DLDESKTOP_VERIFY=1 so it doesn't add network/timer flakiness to the normal suite.
/// Run:  DLDESKTOP_VERIFY=1 dotnet test --filter FullyQualifiedName~ShutdownVerification
/// </summary>
public class ShutdownVerificationTests
{
    [AvaloniaFact]
    public async Task Completing_a_real_download_shows_a_cancelable_shutdown_dialog_and_never_powers_off()
    {
        if (Environment.GetEnvironmentVariable("DLDESKTOP_VERIFY") != "1")
            return; // opt-in

        var poweredOff = false;
        ShutdownService.PowerOffOverride = () => poweredOff = true;

        var payload = new byte[128 * 1024];
        new Random(7).NextBytes(payload);
        using var server = new Loopback(payload);
        var url = server.Url + "verify.bin";

        var dir = Path.Combine(Path.GetTempPath(), "dldesktop_verify_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            // Real config with the feature ON; notifications off so no notify-send is spawned, updates
            // off so no GitHub call. This is the exact path the app uses at runtime.
            var cfg = Config.New();
            cfg.Settings.ShutdownOnCompletion = true;
            cfg.Settings.EnableNotifications = false;
            cfg.Settings.AutoUpdate = false;
            cfg.Settings.DefaultSavePath = dir;
            cfg.Settings.ChunkCount = 4;

            var manager = new DownloadManager();
            // MainViewModel wires manager.AllDownloadsCompleted -> OnAllDownloadsCompleted -> ShutdownService.
            var main = new MainViewModel(new FileStub(cfg), manager);

            // Wait for InitMainViewModelAsync to load the config + initialize the manager.
            await PumpUntil(() => main.Downloads != null, 5000);
            Assert.NotNull(main.Downloads);

            // Add + auto-start a real download through the manager.
            manager.Add(new DownloadItem { Urls = new() { url }, SaveFolder = dir }, autoStart: true);

            // Wait for it to download, complete, and the completion to flow through to ShutdownService.
            await PumpUntil(() => ShutdownService.IsScheduled, 20000);

            Assert.True(ShutdownService.IsScheduled,
                "Completing the only download with ShutdownOnCompletion=on must show the shutdown dialog.");
            Assert.False(poweredOff, "The 30s countdown has not elapsed, so the machine must not be off yet.");

            // The user clicks Cancel → dialog closes, no shutdown.
            ShutdownService.Cancel();
            await PumpUntil(() => !ShutdownService.IsScheduled, 3000);

            Assert.False(ShutdownService.IsScheduled, "Cancel must dismiss the shutdown dialog.");
            Assert.False(poweredOff, "Cancel must prevent the power-off.");
        }
        finally
        {
            ShutdownService.Cancel();
            ShutdownService.PowerOffOverride = null;
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [AvaloniaFact]
    public async Task Countdown_reaching_zero_triggers_power_off()
    {
        if (Environment.GetEnvironmentVariable("DLDESKTOP_VERIFY") != "1")
            return;

        var elapsed = false;
        var canceled = false;
        // 1-second countdown so the test is quick; verifies the timer reaches 0 and fires onElapsed.
        var vm = new ShutdownViewModel(1, onElapsed: () => elapsed = true, onCancel: () => canceled = true);

        await PumpUntil(() => elapsed, 4000);

        Assert.True(elapsed, "The countdown must invoke onElapsed (power off) when it reaches zero.");
        Assert.False(canceled);
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

    private sealed class FileStub : IFileService
    {
        private readonly Config _config;
        public FileStub(Config config) => _config = config;
        public Task<Config> LoadFromFileAsync() => Task.FromResult(_config);
        public Task SaveToFileAsync(Config itemToSave) => Task.CompletedTask;
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

                int start = 0, end = _data.Length - 1;
                var range = ctx.Request.Headers["Range"];
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
