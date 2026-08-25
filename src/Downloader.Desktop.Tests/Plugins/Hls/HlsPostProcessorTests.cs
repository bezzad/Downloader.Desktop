using System.Net;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Plugins.Hls;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins.Hls;

public class HlsPostProcessorTests
{
    private static readonly byte[] Key = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
    private static readonly byte[] Iv = Convert.FromHexString("0f0e0d0c0b0a09080706050403020100");

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Concatenates_unencrypted_segments_in_order()
    {
        using var tmp = new TempDir();
        var a = tmp.WriteBytes("seg0.ts", Encoding.UTF8.GetBytes("AAAA"));
        var b = tmp.WriteBytes("seg1.ts", Encoding.UTF8.GetBytes("BBBB"));
        var recipe = new ConcatRecipe
        {
            Segments = { new SegmentEntry { Encrypted = false }, new SegmentEntry { Encrypted = false } },
        };
        var (proc, ffmpeg) = Build();
        var progress = new ProgressSink();
        var output = Path.Combine(tmp.Path, "out.mp4");

        var result = await proc.ProcessAsync([a, b], Plan(recipe), output, progress, CancellationToken.None);

        Assert.Equal(output, result);
        Assert.True(ffmpeg.WasCalled);
        Assert.Equal("AAAABBBB", File.ReadAllText(result));
        Assert.Equal(1.0, progress.Last, 3);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Decrypts_aes128_segments_then_concatenates()
    {
        using var tmp = new TempDir();
        var p0 = Encoding.UTF8.GetBytes("hello-segment-zero-plaintext");
        var p1 = Encoding.UTF8.GetBytes("hello-segment-one-plaintext!");
        var c0 = tmp.WriteBytes("s0.ts", Aes128.EncryptCbc(p0, Key, Iv));
        var c1 = tmp.WriteBytes("s1.ts", Aes128.EncryptCbc(p1, Key, Iv));
        var recipe = new ConcatRecipe
        {
            Segments =
            {
                new SegmentEntry { Encrypted = true, KeyUri = "https://k/key.bin", IvHex = Convert.ToHexString(Iv) },
                new SegmentEntry { Encrypted = true, KeyUri = "https://k/key.bin", IvHex = Convert.ToHexString(Iv) },
            },
        };
        int keyFetches = 0;
        var (proc, _) = Build(keyFetcher: (_, _, _) => { keyFetches++; return Task.FromResult(Key); });
        var output = Path.Combine(tmp.Path, "out.mp4");

        await proc.ProcessAsync([c0, c1], Plan(recipe), output, new ProgressSink(), CancellationToken.None);

        Assert.Equal(Encoding.UTF8.GetString(p0) + Encoding.UTF8.GetString(p1), File.ReadAllText(output));
        Assert.Equal(1, keyFetches); // key cached across segments with the same URI
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Prepends_init_segment()
    {
        using var tmp = new TempDir();
        var init = tmp.WriteBytes("init.mp4", Encoding.UTF8.GetBytes("INIT"));
        var seg = tmp.WriteBytes("s0.m4s", Encoding.UTF8.GetBytes("DATA"));
        var recipe = new ConcatRecipe { HasInitSegment = true, Segments = { new SegmentEntry() } };
        var (proc, _) = Build();
        var output = Path.Combine(tmp.Path, "out.mp4");

        await proc.ProcessAsync([init, seg], Plan(recipe), output, new ProgressSink(), CancellationToken.None);

        Assert.Equal("INITDATA", File.ReadAllText(output));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Input_count_mismatch_throws()
    {
        using var tmp = new TempDir();
        var a = tmp.WriteBytes("a.ts", [1, 2, 3]);
        var recipe = new ConcatRecipe { Segments = { new SegmentEntry(), new SegmentEntry() } };
        var (proc, _) = Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            proc.ProcessAsync([a], Plan(recipe), Path.Combine(tmp.Path, "o.mp4"), new ProgressSink(), CancellationToken.None));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void CanProcess_handles_concat_and_mux()
    {
        var (proc, _) = Build();
        Assert.True(proc.CanProcess(new PostProcess { Kind = PostProcessKind.Concat }));
        Assert.True(proc.CanProcess(new PostProcess { Kind = PostProcessKind.Mux }));
        Assert.False(proc.CanProcess(PostProcess.None));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Mux_combines_video_and_audio_parts()
    {
        using var tmp = new TempDir();
        var video = tmp.WriteBytes("v.mp4", Encoding.UTF8.GetBytes("VIDEO"));
        var audio = tmp.WriteBytes("a.m4a", Encoding.UTF8.GetBytes("AUDIO"));
        var (proc, ffmpeg) = Build();
        var progress = new ProgressSink();
        var output = Path.Combine(tmp.Path, "muxed.mp4");

        var result = await proc.ProcessAsync(
            [video, audio], new PostProcess { Kind = PostProcessKind.Mux, Recipe = "video+audio" },
            output, progress, CancellationToken.None);

        Assert.Equal(output, result);
        Assert.True(ffmpeg.MuxWasCalled);
        Assert.Equal("VIDEOAUDIO", File.ReadAllText(result)); // stub mux concatenates the two inputs
        Assert.Equal(1.0, progress.Last, 3);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Mux_requires_exactly_two_inputs()
    {
        using var tmp = new TempDir();
        var only = tmp.WriteBytes("v.mp4", Encoding.UTF8.GetBytes("VIDEO"));
        var (proc, _) = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => proc.ProcessAsync(
            [only], new PostProcess { Kind = PostProcessKind.Mux }, Path.Combine(tmp.Path, "o.mp4"),
            new ProgressSink(), CancellationToken.None));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Real_ffmpeg_remux_produces_mp4_when_ffmpeg_available()
    {
        var ffmpegOnPath = FindFfmpeg();
        if (ffmpegOnPath is null)
            return; // gated: skipped where ffmpeg isn't installed (e.g. CI without ffmpeg)

        using var tmp = new TempDir();
        // Generate a tiny real .ts with ffmpeg itself, then remux it through our provider.
        var ts = Path.Combine(tmp.Path, "src.ts");
        await RunRaw(ffmpegOnPath, $"-y -f lavfi -i testsrc=duration=1:size=128x72:rate=10 -c:v libx264 -f mpegts \"{ts}\"");
        Assert.True(File.Exists(ts) && new FileInfo(ts).Length > 0);

        var ffmpeg = new FfmpegBinary(tmp.Path);
        var output = Path.Combine(tmp.Path, "out.mp4");
        await ffmpeg.RemuxAsync(ts, output, CancellationToken.None);

        Assert.True(File.Exists(output));
        Assert.True(new FileInfo(output).Length > 0);
    }

    // ── multi-stream (DASH) recipes ──────────────────────────────────────────────────────────────────

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Two_stream_recipe_concatenates_each_stream_then_muxes()
    {
        using var tmp = new TempDir();
        var vInit = tmp.WriteBytes("v-init.mp4", Encoding.UTF8.GetBytes("VI"));
        var v0 = tmp.WriteBytes("v0.m4s", Encoding.UTF8.GetBytes("V0"));
        var v1 = tmp.WriteBytes("v1.m4s", Encoding.UTF8.GetBytes("V1"));
        var aInit = tmp.WriteBytes("a-init.mp4", Encoding.UTF8.GetBytes("AI"));
        var a0 = tmp.WriteBytes("a0.m4s", Encoding.UTF8.GetBytes("A0"));

        var recipe = new ConcatRecipe
        {
            IntermediateExtension = ".mp4",
            Streams =
            [
                new StreamGroup { HasInitSegment = true, SegmentCount = 2 },
                new StreamGroup { HasInitSegment = true, SegmentCount = 1 },
            ],
            Segments = { new SegmentEntry(), new SegmentEntry(), new SegmentEntry() },
        };
        var (proc, ffmpeg) = Build();
        var progress = new ProgressSink();
        var output = Path.Combine(tmp.Path, "out.mp4");

        var result = await proc.ProcessAsync(
            [vInit, v0, v1, aInit, a0], Plan(recipe), output, progress, CancellationToken.None);

        Assert.True(ffmpeg.MuxWasCalled);
        Assert.False(ffmpeg.WasCalled); // muxed, not remuxed
        // The stub mux concatenates its two inputs, so this proves each stream was assembled separately
        // and handed over in video-then-audio order.
        Assert.Equal("VIV0V1" + "AIA0", File.ReadAllText(result));
        Assert.Equal(1.0, progress.Last, 3);
        // The per-stream intermediates are cleaned up.
        Assert.Empty(Directory.GetFiles(tmp.Path, "*.concat.*"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Whole_file_streams_are_muxed_without_making_a_copy()
    {
        using var tmp = new TempDir();
        var video = tmp.WriteBytes("video.mp4", Encoding.UTF8.GetBytes("VIDEO"));
        var audio = tmp.WriteBytes("audio.mp4", Encoding.UTF8.GetBytes("AUDIO"));
        var recipe = new ConcatRecipe
        {
            IntermediateExtension = ".mp4",
            Streams =
            [
                new StreamGroup { SegmentCount = 1 },
                new StreamGroup { SegmentCount = 1 },
            ],
            Segments = { new SegmentEntry(), new SegmentEntry() },
        };
        var (proc, ffmpeg) = Build();
        var output = Path.Combine(tmp.Path, "out.mp4");

        await proc.ProcessAsync([video, audio], Plan(recipe), output, new ProgressSink(), CancellationToken.None);

        Assert.True(ffmpeg.MuxWasCalled);
        Assert.Equal("VIDEOAUDIO", File.ReadAllText(output));
        // A single complete file per stream needs no intermediate at all — it is fed to ffmpeg in place.
        Assert.Empty(Directory.GetFiles(tmp.Path, "*.concat.*"));
        Assert.Equal(video, ffmpeg.LastMuxVideo);
        Assert.Equal(audio, ffmpeg.LastMuxAudio);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_single_stream_group_still_remuxes_like_before()
    {
        using var tmp = new TempDir();
        var init = tmp.WriteBytes("init.mp4", Encoding.UTF8.GetBytes("INIT"));
        var seg = tmp.WriteBytes("s0.m4s", Encoding.UTF8.GetBytes("DATA"));
        var recipe = new ConcatRecipe
        {
            Streams = [new StreamGroup { HasInitSegment = true, SegmentCount = 1 }],
            Segments = { new SegmentEntry() },
        };
        var (proc, ffmpeg) = Build();
        var output = Path.Combine(tmp.Path, "out.mp4");

        await proc.ProcessAsync([init, seg], Plan(recipe), output, new ProgressSink(), CancellationToken.None);

        Assert.True(ffmpeg.WasCalled);
        Assert.False(ffmpeg.MuxWasCalled);
        Assert.Equal("INITDATA", File.ReadAllText(output));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_recipe_written_before_dash_support_reads_as_one_stream()
    {
        // Exactly the JSON the HLS resolver has always produced — no "Streams" field.
        const string legacy = """
            {"HasInitSegment":true,"OutputExtension":".mp4",
             "Segments":[{"Encrypted":false},{"Encrypted":false}]}
            """;

        var recipe = JsonSerializer.Deserialize<ConcatRecipe>(legacy)!;
        var groups = recipe.StreamsOrSingle();

        Assert.Null(recipe.Streams);
        Assert.Equal(".ts", recipe.IntermediateExtension);
        var group = Assert.Single(groups);
        Assert.True(group.HasInitSegment);
        Assert.Equal(2, group.SegmentCount);
        Assert.Equal(3, group.FileCount);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task More_than_two_streams_is_refused()
    {
        using var tmp = new TempDir();
        var f = tmp.WriteBytes("a.m4s", [1]);
        var recipe = new ConcatRecipe
        {
            Streams =
            [
                new StreamGroup { SegmentCount = 1 },
                new StreamGroup { SegmentCount = 1 },
                new StreamGroup { SegmentCount = 1 },
            ],
            Segments = { new SegmentEntry(), new SegmentEntry(), new SegmentEntry() },
        };
        var (proc, _) = Build();

        await Assert.ThrowsAsync<NotSupportedException>(() => proc.ProcessAsync(
            [f, f, f], Plan(recipe), Path.Combine(tmp.Path, "o.mp4"), new ProgressSink(), CancellationToken.None));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Stream_groups_must_account_for_every_segment_in_the_recipe()
    {
        using var tmp = new TempDir();
        var f = tmp.WriteBytes("a.m4s", [1]);
        var recipe = new ConcatRecipe
        {
            Streams = [new StreamGroup { SegmentCount = 1 }],
            Segments = { new SegmentEntry(), new SegmentEntry() }, // one too many
        };
        var (proc, _) = Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => proc.ProcessAsync(
            [f], Plan(recipe), Path.Combine(tmp.Path, "o.mp4"), new ProgressSink(), CancellationToken.None));
    }

    // ── the key request carries the download's context (issue #7 follow-up) ─────────────────────────

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_key_request_sends_the_recipes_headers_so_a_protected_key_can_be_fetched()
    {
        // Every segment goes out with the download's cookies/referer, but the key is fetched here, at
        // assembly time, out of a bare client — so on a protected origin it was the one request that could
        // fail, at the very end of an otherwise complete download.
        using var server = new KeyServer(Key, requiredCookie: "SID=s3cret", requiredReferer: "https://site.example/watch");
        using var tmp = new TempDir();
        var plain = Encoding.UTF8.GetBytes("hello-segment-zero-plaintext");
        var cipher = tmp.WriteBytes("s0.ts", Aes128.EncryptCbc(plain, Key, Iv));

        ConcatRecipe Recipe(Dictionary<string, string>? keyHeaders) => new()
        {
            KeyHeaders = keyHeaders,
            Segments =
            {
                new SegmentEntry { Encrypted = true, KeyUri = server.KeyUrl, IvHex = Convert.ToHexString(Iv) },
            },
        };

        // With the context: the key is served and the stream assembles.
        var (withContext, _) = Build();
        var output = Path.Combine(tmp.Path, "with.mp4");
        await withContext.ProcessAsync([cipher], Plan(Recipe(new Dictionary<string, string>
        {
            ["Cookie"] = "SID=s3cret",
            ["Referer"] = "https://site.example/watch",
        })), output, new ProgressSink(), CancellationToken.None);

        Assert.Equal(Encoding.UTF8.GetString(plain), File.ReadAllText(output));
        Assert.Contains("SID=s3cret", server.SeenCookies);

        // Without it: the key origin refuses, and the failure surfaces rather than producing a broken file.
        var (bare, _) = Build();
        await Assert.ThrowsAnyAsync<Exception>(() => bare.ProcessAsync(
            [cipher], Plan(Recipe(null)), Path.Combine(tmp.Path, "without.mp4"),
            new ProgressSink(), CancellationToken.None));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_recipe_without_key_headers_round_trips_unchanged()
    {
        // Older recipes (and DASH, whose encrypted manifests are refused as DRM) carry no KeyHeaders.
        var old = JsonSerializer.Deserialize<ConcatRecipe>(
            """{"HasInitSegment":false,"OutputExtension":".mp4","Segments":[{"Encrypted":false}]}""")!;
        Assert.Null(old.KeyHeaders);
        Assert.Single(old.Segments);

        var withHeaders = JsonSerializer.Deserialize<ConcatRecipe>(JsonSerializer.Serialize(new ConcatRecipe
        {
            KeyHeaders = new Dictionary<string, string> { ["Cookie"] = "SID=abc" },
            Segments = { new SegmentEntry() },
        }))!;
        Assert.Equal("SID=abc", withHeaders.KeyHeaders!["Cookie"]);
    }

    /// <summary>A loopback origin that serves the AES key ONLY to a request carrying the expected session.</summary>
    private sealed class KeyServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly byte[] _key;
        private readonly string _cookie;
        private readonly string _referer;

        public List<string> SeenCookies { get; } = new();
        public string KeyUrl { get; }

        public KeyServer(byte[] key, string requiredCookie, string requiredReferer)
        {
            _key = key;
            _cookie = requiredCookie;
            _referer = requiredReferer;
            var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            probe.Start();
            var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            var root = $"http://127.0.0.1:{port}/";
            KeyUrl = root + "key.bin";
            _listener.Prefixes.Add(root);
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

                var cookie = ctx.Request.Headers["Cookie"];
                lock (SeenCookies) SeenCookies.Add(cookie ?? "");
                if (cookie == _cookie && ctx.Request.Headers["Referer"] == _referer)
                {
                    ctx.Response.ContentLength64 = _key.Length;
                    ctx.Response.OutputStream.Write(_key, 0, _key.Length);
                }
                else
                {
                    ctx.Response.StatusCode = 403;
                }
                ctx.Response.OutputStream.Close();
            }
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { /* already stopped */ }
            ((IDisposable)_listener).Dispose();
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────

    private static (HlsPostProcessor, RecordingFfmpeg) Build(
        Func<string, IReadOnlyDictionary<string, string>?, CancellationToken, Task<byte[]>>? keyFetcher = null)
    {
        var ffmpeg = new RecordingFfmpeg();
        var proc = new HlsPostProcessor(ffmpeg, keyFetcher: keyFetcher);
        return (proc, ffmpeg);
    }

    private static PostProcess Plan(ConcatRecipe recipe) =>
        new() { Kind = PostProcessKind.Concat, Recipe = JsonSerializer.Serialize(recipe) };

    private static string? FindFfmpeg()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        var exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        return path.Split(Path.PathSeparator)
            .Select(d => Path.Combine(d.Trim(), exe))
            .FirstOrDefault(File.Exists);
    }

    private static async Task RunRaw(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
    }

    /// <summary>Stub ffmpeg: copies the concatenated input to the output (so tests can assert on bytes).</summary>
    private sealed class RecordingFfmpeg : IFfmpeg
    {
        public bool WasCalled { get; private set; }
        public bool MuxWasCalled { get; private set; }
        public string? LastMuxVideo { get; private set; }
        public string? LastMuxAudio { get; private set; }
        public Task RemuxAsync(string inputFile, string outputPath, CancellationToken cancellationToken)
        {
            WasCalled = true;
            File.Copy(inputFile, outputPath, overwrite: true);
            return Task.CompletedTask;
        }
        public Task MuxAsync(string videoFile, string audioFile, string outputPath, CancellationToken cancellationToken)
        {
            MuxWasCalled = true;
            LastMuxVideo = videoFile;
            LastMuxAudio = audioFile;
            // Concatenate the two inputs so tests can assert both were consumed.
            File.WriteAllBytes(outputPath,
                File.ReadAllBytes(videoFile).Concat(File.ReadAllBytes(audioFile)).ToArray());
            return Task.CompletedTask;
        }
    }

    private sealed class ProgressSink : IProgress<double>
    {
        public double Last { get; private set; }
        public void Report(double value) => Last = value;
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("hls-test-").FullName;
        public string WriteBytes(string name, byte[] bytes)
        {
            var full = System.IO.Path.Combine(Path, name);
            File.WriteAllBytes(full, bytes);
            return full;
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
