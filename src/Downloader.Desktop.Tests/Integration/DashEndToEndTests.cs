using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Plugins.Hls;
using Downloader.Desktop.Plugins.Hls.Dash;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// The whole DASH road, for real: ffmpeg produces a genuine MPEG-DASH stream (separate video and audio
/// adaptation sets, <c>$RepresentationID$</c> and <c>$Number%05d$</c> templates, segment timelines), a
/// loopback server serves it, and the app resolves it, downloads every segment through the real engine,
/// concatenates each stream and muxes them with the real ffmpeg. The output is then probed to prove it is a
/// playable file carrying BOTH streams.
///
/// Unit tests can only show that each piece behaves as designed against fixtures; this one shows the pieces
/// fit. Gated on ffmpeg/ffprobe being present (same convention as
/// <c>Real_ffmpeg_remux_produces_mp4_when_ffmpeg_available</c>) so machines without them still run green.
/// </summary>
public class DashEndToEndTests
{
    [Fact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_real_dash_stream_downloads_and_assembles_into_a_playable_file()
    {
        var ffmpeg = FindOnPath("ffmpeg");
        var ffprobe = FindOnPath("ffprobe");
        if (ffmpeg is null || ffprobe is null)
            return; // gated: no ffmpeg on this machine

        using var work = new TempDir();
        var streamDir = Directory.CreateDirectory(Path.Combine(work.Path, "stream")).FullName;
        await GenerateDashStreamAsync(ffmpeg, streamDir);
        Assert.True(File.Exists(Path.Combine(streamDir, "manifest.mpd")), "ffmpeg did not produce a manifest");

        using var server = new DirectoryServer(streamDir);
        var manifestUrl = server.Url + "manifest.mpd";

        // 1. Resolve: the real manifest → a plan.
        var plan = await new DashResolver(new HttpClient())
            .ResolveAsync(manifestUrl, new ResolveOptions(), CancellationToken.None);

        // 4 video segments + 4 audio segments, each stream behind its own init segment.
        Assert.Equal(10, plan.Parts.Count);
        Assert.All(plan.Parts, p => Assert.StartsWith(server.Url, p.Url));

        // 2. Run: the real plan runner downloads every part through the real engine, then the real
        //    post-processor concatenates each stream and muxes them with the real ffmpeg.
        var downloadDir = Directory.CreateDirectory(Path.Combine(work.Path, "out")).FullName;
        var persisted = PersistedPlan.From(plan);
        var processor = new HlsPostProcessor(new FfmpegBinary(work.Path));
        var stages = new List<string>();

        var finalPath = await new DownloadManager().ExecutePlanAsync(
            persisted, downloadDir, "clip.mp4", processor,
            onPartService: _ => { }, onStage: stages.Add,
            onProgress: _ => { }, isCancelled: () => false, CancellationToken.None);

        Assert.NotNull(finalPath);
        Assert.Equal(Path.Combine(downloadDir, "clip.mp4"), finalPath);
        Assert.True(new FileInfo(finalPath!).Length > 0);
        // Every part was fetched (distinct paths — the engine probes each with a HEAD before the GET).
        var fetched = server.Requested.Where(r => r != "/manifest.mpd").Distinct().ToList();
        Assert.Equal(10, fetched.Count);
        Assert.Contains("/init-stream0.m4s", fetched);
        Assert.Contains("/chunk-stream0-00004.m4s", fetched); // $Number%05d$ padding resolved correctly
        Assert.Contains("/init-stream1.m4s", fetched);
        // The scratch folder and the per-stream intermediates are gone.
        Assert.Empty(Directory.GetDirectories(downloadDir));
        Assert.Empty(Directory.GetFiles(downloadDir).Where(f => f != finalPath));

        // 3. Prove it is playable and complete — the point of the whole feature.
        var streams = await ProbeStreamsAsync(ffprobe, finalPath!);
        Assert.Contains("video", streams);
        Assert.Contains("audio", streams);
        var duration = await ProbeDurationAsync(ffprobe, finalPath!);
        Assert.InRange(duration, 7.0, 9.0); // the source clip is 8 seconds
        // Observed on a real run: 10 parts → a 355 KB MP4, streams [video, audio], duration 8.01s,
        // stages 10 × Plan_Part then Plan_Assembling.
        Assert.NotEmpty(stages);
    }

    // ── the source stream ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Have ffmpeg author a real DASH stream. Its manifest is the genuine article — namespaced, with
    /// <c>contentType</c> adaptation sets, <c>$RepresentationID$</c>/<c>$Number%05d$</c> templates and
    /// per-stream <c>SegmentTimeline</c>s (the video one using <c>r="3"</c>, the audio one four explicit
    /// entries) — which is exactly the variety the parser has to cope with.
    /// </summary>
    private static Task GenerateDashStreamAsync(string ffmpeg, string dir) => RunAsync(ffmpeg,
        "-y -loglevel error " +
        "-f lavfi -i testsrc=duration=8:size=320x180:rate=15 " +
        "-f lavfi -i sine=frequency=440:duration=8 " +
        "-c:v libx264 -preset ultrafast -g 30 -keyint_min 30 -sc_threshold 0 -b:v 300k " +
        "-c:a aac -b:a 64k " +
        "-adaptation_sets \"id=0,streams=v id=1,streams=a\" " +
        "-seg_duration 2 -use_template 1 -use_timeline 1 " +
        "-f dash manifest.mpd",
        workingDirectory: dir);

    // ── probing the result ──────────────────────────────────────────────────────────────────────────

    private static async Task<List<string>> ProbeStreamsAsync(string ffprobe, string file)
    {
        var output = await RunAsync(ffprobe,
            $"-v error -show_entries stream=codec_type -of csv=p=0 \"{file}\"", captureStdout: true);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().TrimEnd(','))
            .Where(l => l.Length > 0)
            .ToList();
    }

    private static async Task<double> ProbeDurationAsync(string ffprobe, string file)
    {
        var output = await RunAsync(ffprobe,
            $"-v error -show_entries format=duration -of csv=p=0 \"{file}\"", captureStdout: true);
        return double.TryParse(output.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static async Task<string> RunAsync(
        string exe, string args, string? workingDirectory = null, bool captureStdout = false)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (workingDirectory is not null) psi.WorkingDirectory = workingDirectory;

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        var err = await stderr;
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(exe)} exited {p.ExitCode}: {err}");
        return captureStdout ? await stdout : string.Empty;
    }

    private static string? FindOnPath(string tool)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        var exe = OperatingSystem.IsWindows() ? tool + ".exe" : tool;
        return path.Split(Path.PathSeparator)
            .Select(d => Path.Combine(d.Trim(), exe))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>Serves a directory over loopback — whatever ffmpeg wrote, at the paths the manifest names.</summary>
    private sealed class DirectoryServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly string _root;
        private readonly List<string> _requested = new();
        private readonly object _gate = new();

        public string Url { get; }

        public IReadOnlyList<string> Requested
        {
            get { lock (_gate) return _requested.ToList(); }
        }

        public DirectoryServer(string root)
        {
            _root = root;
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();

            Url = $"http://127.0.0.1:{port}/";
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
                _ = Task.Run(() => HandleAsync(ctx));
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx)
        {
            var rel = ctx.Request.Url!.AbsolutePath;
            lock (_gate) _requested.Add(rel);

            // Path.GetFileName keeps this inside the served directory whatever the request asks for.
            var file = Path.Combine(_root, Path.GetFileName(rel));
            try
            {
                if (!File.Exists(file))
                {
                    ctx.Response.StatusCode = 404;
                    return;
                }

                var bytes = await File.ReadAllBytesAsync(file);
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = rel.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase)
                    ? "application/dash+xml"
                    : "video/mp4";
                ctx.Response.ContentLength64 = bytes.Length;
                if (ctx.Request.HttpMethod != "HEAD")
                    await ctx.Response.OutputStream.WriteAsync(bytes);
            }
            catch { /* the test asserts on the result, not on transport hiccups */ }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { }
            _listener.Close();
        }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("dash-e2e-").FullName;
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
