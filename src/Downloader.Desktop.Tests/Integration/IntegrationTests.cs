using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Downloader;
using Downloader.Desktop.Models;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// End-to-end integration: serve a small file from a local HTTP server (loopback only, no external
/// network) and download it through the real engine using the app's settings → configuration mapping,
/// then verify the bytes on disk match. Exercises multipart download + range requests + file writing.
/// </summary>
public class IntegrationTests
{
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Downloads_a_small_file_end_to_end_and_matches_bytes()
    {
        var payload = new byte[256 * 1024];
        new Random(1234).NextBytes(payload);

        using var server = new LoopbackFileServer(payload);
        var url = server.Url + "sample.bin";

        var dir = Path.Combine(Path.GetTempPath(), "dldesktop_it_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Use the app's real settings→configuration path (multipart, range-based).
            var settings = DownloadSettings.New();
            settings.ChunkCount = 4;
            settings.ParallelDownload = true;
            settings.MinimumSizeOfChunking = 1024;
            var cfg = settings.ToConfiguration();

            var ct = TestContext.Current.CancellationToken;
            using var service = new DownloadService(cfg);
            await service.DownloadFileTaskAsync(new[] { url }, new DirectoryInfo(dir), ct);

            var file = Directory.GetFiles(dir).Single();
            var got = await File.ReadAllBytesAsync(file, ct);

            Assert.Equal(payload.Length, got.Length);
            Assert.True(payload.SequenceEqual(got), "Downloaded bytes must match the served file.");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>Minimal loopback HTTP server that serves one byte payload with Range support.</summary>
    private sealed class LoopbackFileServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly byte[] _data;

        public string Url { get; }

        public LoopbackFileServer(byte[] data)
        {
            _data = data;
            var port = FreePort();
            Url = $"http://127.0.0.1:{port}/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(Url);
            _listener.Start();
            _ = Task.Run(LoopAsync);
        }

        private static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
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
