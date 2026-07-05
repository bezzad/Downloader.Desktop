using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests;

/// <summary>
/// Exercises the multi-part plan runner core (<see cref="DownloadManager.ExecutePlanAsync"/>) against a
/// loopback server serving distinct bytes per path — no external network, no UI/queue machinery.
/// </summary>
public class PlanRunnerTests
{
    private static PersistedPlan Plan(string baseUrl, PostProcessKind post, params string[] names) => new()
    {
        SuggestedFileName = "out.bin",
        PostProcessKind = post,
        Parts = names.Select(n => new PersistedPart { Url = baseUrl + n }).ToList()
    };

    [Fact]
    public async Task Happy_path_downloads_all_parts_in_order_assembles_and_cleans_up()
    {
        var parts = new Dictionary<string, byte[]>
        {
            ["a.ts"] = Bytes("AAAA", 5000),
            ["b.ts"] = Bytes("BBBB", 6000),
            ["c.ts"] = Bytes("CCCC", 7000)
        };
        using var server = new LoopbackServer(parts);
        var dir = TempDir();
        try
        {
            var mgr = new DownloadManager();
            var processor = new ConcatProcessor(); // stand-in for the HLS concat/mux post-processor
            var stages = new List<string>();

            var finalPath = await mgr.ExecutePlanAsync(
                Plan(server.Url, PostProcessKind.Concat, "a.ts", "b.ts", "c.ts"),
                dir, "video.mp4", processor,
                onPartService: _ => { }, onStage: s => stages.Add(s),
                onProgress: _ => { }, isCancelled: () => false, CancellationToken.None);

            Assert.Equal(Path.Combine(dir, "video.mp4"), finalPath);
            Assert.True(File.Exists(finalPath));
            // Assembled = a + b + c in order.
            var expected = parts["a.ts"].Concat(parts["b.ts"]).Concat(parts["c.ts"]).ToArray();
            var got = await File.ReadAllBytesAsync(finalPath);
            Assert.True(expected.SequenceEqual(got));
            // Parts scratch folder is gone after a successful assemble.
            Assert.False(Directory.Exists(Path.Combine(dir, ".video.mp4.parts")));
            Assert.Contains(stages, s => s.Contains("Assembl", StringComparison.OrdinalIgnoreCase));
        }
        finally { TryDelete(dir); }
    }

    [Fact]
    public async Task Per_part_headers_reach_the_server()
    {
        var parts = new Dictionary<string, byte[]> { ["seg.ts"] = Bytes("X", 2000) };
        using var server = new LoopbackServer(parts);
        var dir = TempDir();
        try
        {
            var plan = new PersistedPlan
            {
                PostProcessKind = PostProcessKind.None,
                Parts = new List<PersistedPart>
                {
                    new() { Url = server.Url + "seg.ts", Headers = new Dictionary<string, string> { ["X-Token"] = "secret123" } }
                }
            };
            await new DownloadManager().ExecutePlanAsync(plan, dir, "f.bin", null,
                _ => { }, _ => { }, _ => { }, () => false, CancellationToken.None);

            Assert.Equal("secret123", server.LastHeaders["seg.ts"]["X-Token"]);
        }
        finally { TryDelete(dir); }
    }

    [Fact]
    public async Task Restart_resume_only_fetches_missing_parts()
    {
        var parts = new Dictionary<string, byte[]>
        {
            ["a.ts"] = Bytes("AAAA", 4000),
            ["b.ts"] = Bytes("BBBB", 4000)
        };
        using var server = new LoopbackServer(parts);
        var dir = TempDir();
        try
        {
            // Simulate a previous run that already finished part 0 (index 0000_a.ts) on disk.
            var partsDir = Path.Combine(dir, ".out.mp4.parts");
            Directory.CreateDirectory(partsDir);
            File.WriteAllBytes(Path.Combine(partsDir, "0000_a.ts"), parts["a.ts"]);
            File.WriteAllText(Path.Combine(partsDir, "0000_a.ts.done"), "1"); // no expected size → .done marker

            var mgr = new DownloadManager();
            await mgr.ExecutePlanAsync(Plan(server.Url, PostProcessKind.None, "a.ts", "b.ts"),
                dir, "out.mp4", null, _ => { }, _ => { }, _ => { }, () => false, CancellationToken.None);

            // Only b.ts was fetched; a.ts was reused from disk.
            Assert.False(server.Requested.Contains("a.ts"));
            Assert.True(server.Requested.Contains("b.ts"));
            var final = Path.Combine(dir, "out.mp4");
            var got = await File.ReadAllBytesAsync(final);
            Assert.True(parts["a.ts"].Concat(parts["b.ts"]).SequenceEqual(got));
        }
        finally { TryDelete(dir); }
    }

    [Fact]
    public async Task Cancel_removes_the_parts_folder_and_returns_null()
    {
        var parts = new Dictionary<string, byte[]> { ["a.ts"] = Bytes("A", 3000), ["b.ts"] = Bytes("B", 3000) };
        using var server = new LoopbackServer(parts);
        var dir = TempDir();
        try
        {
            var mgr = new DownloadManager();
            // Report cancelled right after the first part completes.
            var result = await mgr.ExecutePlanAsync(Plan(server.Url, PostProcessKind.None, "a.ts", "b.ts"),
                dir, "c.mp4", null, _ => { }, _ => { }, _ => { }, isCancelled: () => true, CancellationToken.None);

            Assert.Null(result);
            Assert.False(Directory.Exists(Path.Combine(dir, ".c.mp4.parts")));
            Assert.False(File.Exists(Path.Combine(dir, "c.mp4")));
        }
        finally { TryDelete(dir); }
    }

    [Fact]
    public async Task Missing_post_processor_throws_and_keeps_parts_for_retry()
    {
        var parts = new Dictionary<string, byte[]> { ["a.ts"] = Bytes("A", 2000), ["b.ts"] = Bytes("B", 2000) };
        using var server = new LoopbackServer(parts);
        var dir = TempDir();
        try
        {
            var mgr = new DownloadManager();
            // Plan needs a Mux post-process but we pass no processor → must throw, keeping parts.
            await Assert.ThrowsAnyAsync<Exception>(() => mgr.ExecutePlanAsync(
                Plan(server.Url, PostProcessKind.Mux, "a.ts", "b.ts"),
                dir, "m.mp4", processor: null, _ => { }, _ => { }, _ => { }, () => false, CancellationToken.None));

            // Parts folder is kept (both parts downloaded before the assemble step failed).
            var partsDir = Path.Combine(dir, ".m.mp4.parts");
            Assert.True(Directory.Exists(partsDir));
            Assert.Equal(2, Directory.GetFiles(partsDir).Count(f => !f.EndsWith(".done")));
        }
        finally { TryDelete(dir); }
    }

    [Fact]
    public void Single_part_none_plan_does_not_need_the_runner()
    {
        // The Start branch: single part + PostProcess.None keeps today's legacy engine path (no parts folder).
        var single = new PersistedPlan { PostProcessKind = PostProcessKind.None, Parts = { new PersistedPart { Url = "https://h/f" } } };
        Assert.False(single.NeedsRunner);

        var multi = new PersistedPlan { PostProcessKind = PostProcessKind.None, Parts = { new PersistedPart { Url = "a" }, new PersistedPart { Url = "b" } } };
        Assert.True(multi.NeedsRunner);

        var post = new PersistedPlan { PostProcessKind = PostProcessKind.Mux, Parts = { new PersistedPart { Url = "a" } } };
        Assert.True(post.NeedsRunner);
    }

    // ---- HLS perf + assembly-naming fixes (fix-hls-segment-perf-and-assembly) ----

    [Fact]
    public void Segment_and_small_parts_are_single_chunk()
    {
        // Segments always single-chunk (their size is usually unknown); known-small parts too;
        // big non-segment parts keep the user's full multipart config.
        Assert.True(DownloadManager.IsSingleChunkPart(new PersistedPart { Kind = PartKind.Segment }));
        Assert.True(DownloadManager.IsSingleChunkPart(new PersistedPart { Kind = PartKind.Segment, ExpectedSize = 900_000_000 }));
        Assert.True(DownloadManager.IsSingleChunkPart(new PersistedPart { Kind = PartKind.Combined, ExpectedSize = 1024 }));
        Assert.False(DownloadManager.IsSingleChunkPart(new PersistedPart { Kind = PartKind.Video, ExpectedSize = 900_000_000 }));
        Assert.False(DownloadManager.IsSingleChunkPart(new PersistedPart { Kind = PartKind.Combined })); // unknown size, not a segment
    }

    [Fact]
    public void Assembling_path_keeps_the_extension_last()
    {
        // ffmpeg picks its muxer from the extension: "x.mp4.assembling" fails, "x.assembling.mp4" works.
        Assert.Equal(Path.Combine("d", "video.assembling.mp4"), DownloadManager.AssemblingPath(Path.Combine("d", "video.mp4")));
        Assert.Equal(Path.Combine("d", "noext.assembling"), DownloadManager.AssemblingPath(Path.Combine("d", "noext")));
    }

    [Fact]
    public void Playlist_final_names_normalize_to_media_extensions()
    {
        var mux = new PersistedPlan { PostProcessKind = PostProcessKind.Mux };
        // The author-hit case: name auto-derived from the playlist URL.
        Assert.Equal("skate_phantom_flex_4k.mp4", DownloadManager.NormalizeAssembledName("skate_phantom_flex_4k.m3u8", mux));
        Assert.Equal("x.mp4", DownloadManager.NormalizeAssembledName("x.m3u", mux));
        Assert.Equal("x.mp4", DownloadManager.NormalizeAssembledName("x", mux));
        // A concrete user extension is preserved; the plugin's suggested extension wins over .mp4.
        Assert.Equal("mine.mkv", DownloadManager.NormalizeAssembledName("mine.mkv", mux));
        var suggested = new PersistedPlan { PostProcessKind = PostProcessKind.Mux, SuggestedFileName = "out.webm" };
        Assert.Equal("video.webm", DownloadManager.NormalizeAssembledName("video.m3u8", suggested));
        // No post-process → playlist names stay untouched (the download IS the playlist).
        var none = new PersistedPlan { PostProcessKind = PostProcessKind.None };
        Assert.Equal("list.m3u8", DownloadManager.NormalizeAssembledName("list.m3u8", none));
    }

    [Fact]
    public async Task Parallel_segments_assemble_in_order()
    {
        // 6 segment parts (parallel mode) with distinct bytes → the concat output must be in index order.
        var parts = Enumerable.Range(0, 6).ToDictionary(i => $"s{i}.ts", i => Bytes($"SEG{i}-", 3000 + i * 100));
        using var server = new LoopbackServer(parts);
        var dir = TempDir();
        try
        {
            var plan = new PersistedPlan
            {
                PostProcessKind = PostProcessKind.None,
                Parts = Enumerable.Range(0, 6)
                    .Select(i => new PersistedPart { Url = server.Url + $"s{i}.ts", Kind = PartKind.Segment })
                    .ToList()
            };
            var final = await new DownloadManager().ExecutePlanAsync(plan, dir, "joined.bin", null,
                _ => { }, _ => { }, _ => { }, () => false, CancellationToken.None);

            var expected = Enumerable.Range(0, 6).SelectMany(i => parts[$"s{i}.ts"]).ToArray();
            var got = await File.ReadAllBytesAsync(final);
            Assert.True(expected.SequenceEqual(got), "parallel-downloaded segments must assemble in index order");
        }
        finally { TryDelete(dir); }
    }

    [Fact]
    public async Task Parallel_segments_actually_download_concurrently()
    {
        // Author-reported: segments appeared to download serially. Prove the bounded-parallel loop
        // really overlaps requests: a slow server (250 ms/request) tracks the max in-flight count.
        var parts = Enumerable.Range(0, 8).ToDictionary(i => $"p{i}.ts", i => Bytes($"P{i}", 2000));
        using var server = new LoopbackServer(parts) { ResponseDelay = TimeSpan.FromMilliseconds(250) };
        var dir = TempDir();
        try
        {
            var plan = new PersistedPlan
            {
                PostProcessKind = PostProcessKind.None,
                Parts = Enumerable.Range(0, 8)
                    .Select(i => new PersistedPart { Url = server.Url + $"p{i}.ts", Kind = PartKind.Segment })
                    .ToList()
            };
            await new DownloadManager().ExecutePlanAsync(plan, dir, "c.bin", null,
                _ => { }, _ => { }, _ => { }, () => false, CancellationToken.None);

            Assert.True(server.MaxConcurrent >= 3,
                $"expected >=3 overlapping segment requests, saw {server.MaxConcurrent} — the parallel path is not engaging");
        }
        finally { TryDelete(dir); }
    }

    [Fact]
    public void Persisted_plan_round_trips_through_json()
    {
        var plan = new DownloadPlan
        {
            SuggestedFileName = "movie.mp4",
            PostProcess = new PostProcess { Kind = PostProcessKind.Mux, Recipe = "-c copy" },
            Parts = new[]
            {
                new DownloadPart { Url = "https://h/v.m4s", Kind = PartKind.Video, ExpectedSize = 100, Headers = new Dictionary<string, string> { ["A"] = "1" } },
                new DownloadPart { Url = "https://h/a.m4s", Kind = PartKind.Audio }
            }
        };
        var back = PersistedPlan.FromJson(PersistedPlan.From(plan).ToJson());

        Assert.Equal("movie.mp4", back.SuggestedFileName);
        Assert.Equal(PostProcessKind.Mux, back.PostProcessKind);
        Assert.Equal("-c copy", back.PostProcessRecipe);
        Assert.Equal(2, back.Parts.Count);
        Assert.Equal("https://h/v.m4s", back.Parts[0].Url);
        Assert.Equal(100, back.Parts[0].ExpectedSize);
        Assert.Equal("1", back.Parts[0].Headers["A"]);
        Assert.True(back.NeedsRunner);
    }

    // ---- helpers ----

    private static byte[] Bytes(string tag, int len)
    {
        var b = new byte[len];
        var t = Encoding.ASCII.GetBytes(tag);
        for (var i = 0; i < len; i++) b[i] = t[i % t.Length];
        return b;
    }

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "dldesktop_plan_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
    }

    /// <summary>A no-op post-processor that just concatenates the input files (like the real concat step).</summary>
    private sealed class ConcatProcessor : IPostProcessor
    {
        public bool CanProcess(PostProcess plan) => true;
        public async Task<string> ProcessAsync(IReadOnlyList<string> inputFiles, PostProcess plan,
            string outputPath, IProgress<double> progress, CancellationToken cancellationToken)
        {
            await using var outStream = File.Create(outputPath);
            foreach (var f in inputFiles)
                await using (var inStream = File.OpenRead(f))
                    await inStream.CopyToAsync(outStream, cancellationToken);
            progress?.Report(1.0);
            return outputPath;
        }
    }

    /// <summary>Loopback server: serves distinct bytes per path, records which paths were requested and the
    /// headers of the last request for each path.</summary>
    private sealed class LoopbackServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly Dictionary<string, byte[]> _files;
        private int _inFlight;
        private int _maxConcurrent;
        public ConcurrentBag<string> Requested { get; } = new();
        public ConcurrentDictionary<string, Dictionary<string, string>> LastHeaders { get; } = new();
        public string Url { get; }

        /// <summary>Optional artificial latency per request (lets tests observe request overlap).</summary>
        public TimeSpan ResponseDelay { get; init; } = TimeSpan.Zero;

        /// <summary>The highest number of simultaneously in-flight requests the server observed.</summary>
        public int MaxConcurrent => _maxConcurrent;

        public LoopbackServer(Dictionary<string, byte[]> files)
        {
            _files = files;
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
            var current = System.Threading.Interlocked.Increment(ref _inFlight);
            int seen;
            while (current > (seen = _maxConcurrent) &&
                   System.Threading.Interlocked.CompareExchange(ref _maxConcurrent, current, seen) != seen) { }
            if (ResponseDelay > TimeSpan.Zero)
                System.Threading.Thread.Sleep(ResponseDelay);
            try
            {
                var name = ctx.Request.Url!.AbsolutePath.TrimStart('/');
                var headers = ctx.Request.Headers.AllKeys.ToDictionary(k => k!, k => ctx.Request.Headers[k]);
                LastHeaders[name] = headers;
                if (ctx.Request.HttpMethod != "HEAD")
                    Requested.Add(name);

                var resp = ctx.Response;
                resp.Headers["Accept-Ranges"] = "bytes";
                if (!_files.TryGetValue(name, out var data))
                {
                    resp.StatusCode = 404;
                    resp.OutputStream.Close();
                    return;
                }

                if (ctx.Request.HttpMethod == "HEAD")
                {
                    resp.ContentLength64 = data.Length;
                    resp.OutputStream.Close();
                    return;
                }

                int start = 0, end = data.Length - 1;
                var range = ctx.Request.Headers["Range"];
                if (!string.IsNullOrEmpty(range) && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
                {
                    var seg = range.Substring(6).Split('-');
                    if (seg.Length == 2)
                    {
                        if (int.TryParse(seg[0], out var s)) start = s;
                        if (int.TryParse(seg[1], out var e)) end = e;
                    }
                    end = Math.Min(end, data.Length - 1);
                    start = Math.Max(0, Math.Min(start, end));
                    resp.StatusCode = 206;
                    resp.AddHeader("Content-Range", $"bytes {start}-{end}/{data.Length}");
                }

                var len = end - start + 1;
                resp.ContentLength64 = len;
                resp.OutputStream.Write(data, start, len);
                resp.OutputStream.Close();
            }
            catch { try { ctx.Response.Abort(); } catch { /* ignore */ } }
            finally { System.Threading.Interlocked.Decrement(ref _inFlight); }
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { /* ignore */ }
            try { _listener.Close(); } catch { /* ignore */ }
        }
    }
}
