using System.Text;
using System.Text.Json;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Plugins.Hls;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins.Hls;

/// <summary>
/// A master playlist whose video variants point at an <c>#EXT-X-MEDIA</c> audio group carries NO audio in
/// the variant itself — YouTube and many CDN (x.com) masters are shaped that way. Downloading only the
/// selected <c>#EXT-X-STREAM-INF</c> rendition therefore produced a file that plays but is silent. These
/// tests pin the whole chain: the parser reading the renditions, the resolver adding the audio stream as a
/// second concat group, the post-processor muxing the two, and the ffmpeg arguments that carry the audio
/// into the output.
/// </summary>
public class HlsSeparateAudioTests
{
    private readonly M3u8Parser _parser = new();
    private static readonly Uri MasterBase = new("https://cdn.example.com/video/master.m3u8");

    // ---------- parser ----------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Parser_reads_ext_x_media_renditions()
    {
        var master = _parser.ParseMaster(TestFixtures.Read("master-audio.m3u8"), MasterBase);

        Assert.Equal(2, master.Variants.Count);
        Assert.Equal(3, master.Renditions.Count);

        var english = master.Renditions[0];
        Assert.Equal("AUDIO", english.Type);
        Assert.Equal("aud", english.GroupId);
        Assert.Equal("English", english.Name);
        Assert.Equal("en", english.Language);
        Assert.True(english.IsDefault);
        // Relative rendition URIs resolve against the master's own address.
        Assert.Equal("https://cdn.example.com/video/audio/en/index.m3u8", english.Uri);

        Assert.False(master.Renditions[1].IsDefault);
        Assert.Equal("SUBTITLES", master.Renditions[2].Type);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Parser_reads_the_variant_audio_group_and_codecs()
    {
        var master = _parser.ParseMaster(TestFixtures.Read("master-audio.m3u8"), MasterBase);

        var best = master.Best();
        Assert.Equal(4_800_000, best.Bandwidth);
        Assert.Equal("aud", best.AudioGroupId);
        Assert.Equal("avc1.640028", best.Codecs);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void AudioFor_picks_the_default_rendition_of_the_variants_group()
    {
        var master = _parser.ParseMaster(TestFixtures.Read("master-audio.m3u8"), MasterBase);

        var audio = master.AudioFor(master.Best());

        Assert.NotNull(audio);
        Assert.Equal("English", audio!.Name);
        Assert.Equal("https://cdn.example.com/video/audio/en/index.m3u8", audio.Uri);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void AudioFor_ignores_a_group_from_another_variant_and_non_audio_types()
    {
        const string text =
            "#EXTM3U\n" +
            "#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"other\",NAME=\"English\",DEFAULT=YES,URI=\"a/index.m3u8\"\n" +
            "#EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID=\"aud\",NAME=\"English\",DEFAULT=YES,URI=\"s/index.m3u8\"\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=800000,CODECS=\"avc1.4d401e\",AUDIO=\"aud\"\nv/index.m3u8\n";

        var master = _parser.ParseMaster(text, MasterBase);

        Assert.Null(master.AudioFor(master.Best()));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void AudioFor_skips_a_rendition_that_has_no_uri()
    {
        // No URI means the audio is muxed into the variant itself — there is nothing extra to download.
        const string text =
            "#EXTM3U\n" +
            "#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"aud\",NAME=\"English\",DEFAULT=YES\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=800000,CODECS=\"avc1.4d401e,mp4a.40.2\",AUDIO=\"aud\"\nv/index.m3u8\n";

        var master = _parser.ParseMaster(text, MasterBase);

        Assert.Null(master.AudioFor(master.Best()));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void AudioFor_returns_nothing_for_a_variant_that_already_has_audio()
    {
        // A variant with no AUDIO attribute whose CODECS names an audio codec is self-contained.
        const string text =
            "#EXTM3U\n" +
            "#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"aud\",NAME=\"English\",DEFAULT=YES,URI=\"a/index.m3u8\"\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=800000,CODECS=\"avc1.4d401e,mp4a.40.2\"\nv/index.m3u8\n";

        var master = _parser.ParseMaster(text, MasterBase);

        Assert.Null(master.AudioFor(master.Best()));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void AudioFor_uses_the_default_rendition_when_a_video_only_variant_names_no_group()
    {
        // Some masters omit AUDIO on the variant even though its CODECS proves it is video-only.
        const string text =
            "#EXTM3U\n" +
            "#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"aud\",NAME=\"English\",DEFAULT=YES,URI=\"a/index.m3u8\"\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=800000,CODECS=\"avc1.4d401e\"\nv/index.m3u8\n";

        var master = _parser.ParseMaster(text, MasterBase);

        Assert.Equal("https://cdn.example.com/video/a/index.m3u8", master.AudioFor(master.Best())?.Uri);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("avc1.4d401e", true)]
    [InlineData("avc1.640028,mp4a.40.2", false)]
    [InlineData("vp09.00.10.08,opus", false)]
    [InlineData("hvc1.1.6.L93.B0, ec-3", false)]
    [InlineData("", false)]           // an absent CODECS list proves nothing
    [InlineData(null, false)]
    public void DeclaresNoAudio_only_treats_an_explicit_codec_list_as_proof(string? codecs, bool expected)
    {
        Assert.Equal(expected, HlsMasterPlaylist.DeclaresNoAudio(codecs));
    }

    // ---------- resolver ----------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task ResolveAsync_downloads_the_separate_audio_rendition_as_a_second_stream()
    {
        const string master =
            "#EXTM3U\n" +
            "#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"aud\",NAME=\"English\",DEFAULT=YES,URI=\"audio/index.m3u8\"\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=4800000,RESOLUTION=1920x1080,CODECS=\"avc1.640028\",AUDIO=\"aud\"\n" +
            "high/index.m3u8\n";
        const string video =
            "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:0\n#EXTINF:6.0,\na.ts\n#EXTINF:6.0,\nb.ts\n#EXT-X-ENDLIST\n";
        const string audio =
            "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:0\n#EXTINF:6.0,\na.aac\n#EXT-X-ENDLIST\n";
        using var server = new LoopbackServer()
            .MapText("/master.m3u8", master)
            .MapText("/high/index.m3u8", video)
            .MapText("/audio/index.m3u8", audio);
        using var http = new HttpClient();
        var resolver = new HlsResolver(http);

        var plan = await resolver.ResolveAsync(server.Url("master.m3u8"), CancellationToken.None);

        // Video segments first, then the audio stream's — the order the recipe's groups describe.
        Assert.Equal(
            new[] { server.Url("high/a.ts"), server.Url("high/b.ts"), server.Url("audio/a.aac") },
            plan.Parts.Select(p => p.Url));

        var recipe = JsonSerializer.Deserialize<ConcatRecipe>(plan.PostProcess.Recipe!)!;
        Assert.NotNull(recipe.Streams);
        Assert.Equal(2, recipe.Streams!.Count);
        Assert.Equal(2, recipe.Streams[0].SegmentCount);
        Assert.Equal(1, recipe.Streams[1].SegmentCount);
        Assert.All(recipe.Streams, g => Assert.False(g.HasInitSegment));
        Assert.Equal(3, recipe.Segments.Count);
        Assert.Equal(PostProcessKind.Concat, plan.PostProcess.Kind);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task ResolveAsync_carries_the_init_segment_of_each_stream_and_labels_fmp4()
    {
        const string master =
            "#EXTM3U\n" +
            "#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"aud\",NAME=\"English\",DEFAULT=YES,URI=\"audio/index.m3u8\"\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=4800000,CODECS=\"avc1.640028\",AUDIO=\"aud\"\nhigh/index.m3u8\n";
        const string video =
            "#EXTM3U\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:6.0,\na.m4s\n#EXT-X-ENDLIST\n";
        const string audio =
            "#EXTM3U\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:6.0,\na.m4s\n#EXT-X-ENDLIST\n";
        using var server = new LoopbackServer()
            .MapText("/master.m3u8", master)
            .MapText("/high/index.m3u8", video)
            .MapText("/audio/index.m3u8", audio);
        using var http = new HttpClient();
        var resolver = new HlsResolver(http);

        var plan = await resolver.ResolveAsync(server.Url("master.m3u8"), CancellationToken.None);

        Assert.Equal(
            new[]
            {
                server.Url("high/init.mp4"), server.Url("high/a.m4s"),
                server.Url("audio/init.mp4"), server.Url("audio/a.m4s"),
            },
            plan.Parts.Select(p => p.Url));

        var recipe = JsonSerializer.Deserialize<ConcatRecipe>(plan.PostProcess.Recipe!)!;
        Assert.All(recipe.Streams!, g =>
        {
            Assert.True(g.HasInitSegment);
            Assert.Equal(1, g.SegmentCount);
            Assert.Equal(2, g.FileCount);
        });
        // fMP4 segments must not be handed to ffmpeg labelled ".ts".
        Assert.Equal(".mp4", recipe.IntermediateExtension);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task ResolveAsync_stamps_the_headers_onto_the_audio_parts_too()
    {
        const string master =
            "#EXTM3U\n" +
            "#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"aud\",NAME=\"English\",DEFAULT=YES,URI=\"audio/index.m3u8\"\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=4800000,CODECS=\"avc1.640028\",AUDIO=\"aud\"\nhigh/index.m3u8\n";
        const string one = "#EXTM3U\n#EXTINF:6.0,\na.ts\n#EXT-X-ENDLIST\n";
        using var server = new LoopbackServer()
            .MapText("/master.m3u8", master)
            .MapText("/high/index.m3u8", one)
            .MapText("/audio/index.m3u8", one);
        using var http = new HttpClient();
        var resolver = new HlsResolver(http);
        var options = new ResolveOptions
        {
            Headers = new Dictionary<string, string> { ["Referer"] = "https://site.example/watch/42" },
        };

        var plan = await resolver.ResolveAsync(server.Url("master.m3u8"), options, CancellationToken.None);

        Assert.Equal(2, plan.Parts.Count);
        Assert.All(plan.Parts, p => Assert.Equal("https://site.example/watch/42", p.Headers!["Referer"]));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task ResolveAsync_leaves_a_self_contained_variant_as_a_single_stream()
    {
        // Regression guard: a master without audio renditions must keep producing the one-group recipe
        // (Streams == null) that every earlier version wrote.
        const string master =
            "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=4800000,CODECS=\"avc1.640028,mp4a.40.2\"\nhigh/index.m3u8\n";
        const string video = "#EXTM3U\n#EXTINF:6.0,\na.ts\n#EXTINF:6.0,\nb.ts\n#EXT-X-ENDLIST\n";
        using var server = new LoopbackServer()
            .MapText("/master.m3u8", master)
            .MapText("/high/index.m3u8", video);
        using var http = new HttpClient();
        var resolver = new HlsResolver(http);

        var plan = await resolver.ResolveAsync(server.Url("master.m3u8"), CancellationToken.None);

        var recipe = JsonSerializer.Deserialize<ConcatRecipe>(plan.PostProcess.Recipe!)!;
        Assert.Equal(2, plan.Parts.Count);
        Assert.Null(recipe.Streams);
        Assert.Equal(".ts", recipe.IntermediateExtension);
        Assert.Single(recipe.StreamsOrSingle());
    }

    // ---------- resolver → post-processor round trip ----------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_resolved_plan_assembles_by_muxing_video_with_audio()
    {
        const string master =
            "#EXTM3U\n" +
            "#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"aud\",NAME=\"English\",DEFAULT=YES,URI=\"audio/index.m3u8\"\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=4800000,CODECS=\"avc1.640028\",AUDIO=\"aud\"\nhigh/index.m3u8\n";
        const string video =
            "#EXTM3U\n#EXTINF:6.0,\na.ts\n#EXTINF:6.0,\nb.ts\n#EXT-X-ENDLIST\n";
        const string audio =
            "#EXTM3U\n#EXTINF:6.0,\na.aac\n#EXT-X-ENDLIST\n";
        using var server = new LoopbackServer()
            .MapText("/master.m3u8", master)
            .MapText("/high/index.m3u8", video)
            .MapText("/audio/index.m3u8", audio);
        using var http = new HttpClient();
        var plan = await new HlsResolver(http).ResolveAsync(server.Url("master.m3u8"), CancellationToken.None);

        using var dir = new TempFolder();
        // One downloaded file per part, in plan order, with recognisable bytes.
        var inputs = new List<string>();
        for (int i = 0; i < plan.Parts.Count; i++)
        {
            var path = Path.Combine(dir.Path, $"part{i}.bin");
            await File.WriteAllTextAsync(path, $"[{i}]", TestContext.Current.CancellationToken);
            inputs.Add(path);
        }

        var ffmpeg = new RecordingFfmpeg();
        var output = Path.Combine(dir.Path, "video.mp4");
        var processor = new HlsPostProcessor(ffmpeg);

        await processor.ProcessAsync(inputs, plan.PostProcess, output, new Sink(), CancellationToken.None);

        Assert.True(ffmpeg.MuxWasCalled);
        Assert.False(ffmpeg.RemuxWasCalled);
        // The video group's two segments were concatenated into the mux's first input, the audio
        // group's single segment into the second — audio really reaches the output.
        Assert.Equal("[0][1]", ffmpeg.MuxedVideo);
        Assert.Equal("[2]", ffmpeg.MuxedAudio);
        Assert.True(File.Exists(output));
    }

    // ---------- ffmpeg arguments ----------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Mux_arguments_map_the_video_and_the_audio_input_explicitly()
    {
        // Without explicit maps ffmpeg picks one stream per type across BOTH inputs, so a video file with
        // a stray audio track can win and the real audio is silently dropped.
        var args = FfmpegBinary.BuildMuxArgs("/tmp/v.mp4", "/tmp/a.m4a", "/tmp/out.mp4");

        Assert.Contains("-map 0:v:0", args);
        Assert.Contains("-map 1:a:0", args);
        Assert.Contains("-c copy", args);
        Assert.Contains("\"/tmp/v.mp4\"", args);
        Assert.Contains("\"/tmp/a.m4a\"", args);
        Assert.Contains("\"/tmp/out.mp4\"", args);
        // MP4/fMP4 audio already carries its ASC; filtering it would be wrong.
        Assert.DoesNotContain("aac_adtstoasc", args);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("/tmp/a.s1.concat.ts", true)]
    [InlineData("/tmp/a.aac", true)]
    [InlineData("/tmp/a.s1.concat.mp4", false)]
    [InlineData("/tmp/a.m4a", false)]
    public void Mux_adds_the_adts_filter_only_for_transport_stream_audio(string audioFile, bool expected)
    {
        // AAC inside MPEG-TS is ADTS-framed and is not legal in MP4 without this bitstream filter.
        var args = FfmpegBinary.BuildMuxArgs("/tmp/v.ts", audioFile, "/tmp/out.mp4");

        Assert.Equal(expected, args.Contains("-bsf:a aac_adtstoasc"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_site_media_muxer_maps_its_inputs_explicitly_too()
    {
        var args = Downloader.Desktop.Plugins.SiteMedia.FfmpegMuxer
            .BuildMuxArgs("/tmp/v.mp4", "/tmp/a.webm", "/tmp/out.mp4");

        Assert.Contains("-map 0:v:0", args);
        Assert.Contains("-map 1:a:0", args);
        Assert.Contains("-c copy", args);
    }

    private sealed class RecordingFfmpeg : IFfmpeg
    {
        public bool RemuxWasCalled { get; private set; }
        public bool MuxWasCalled { get; private set; }
        public string? MuxedVideo { get; private set; }
        public string? MuxedAudio { get; private set; }

        public Task RemuxAsync(string inputFile, string outputPath, CancellationToken cancellationToken)
        {
            RemuxWasCalled = true;
            File.Copy(inputFile, outputPath, overwrite: true);
            return Task.CompletedTask;
        }

        public Task MuxAsync(string videoFile, string audioFile, string outputPath, CancellationToken cancellationToken)
        {
            MuxWasCalled = true;
            MuxedVideo = File.ReadAllText(videoFile, Encoding.UTF8);
            MuxedAudio = File.ReadAllText(audioFile, Encoding.UTF8);
            File.WriteAllText(outputPath, MuxedVideo + MuxedAudio);
            return Task.CompletedTask;
        }
    }

    private sealed class Sink : IProgress<double>
    {
        public void Report(double value) { }
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "dldesktop-hls-audio-" + Guid.NewGuid().ToString("N"));

        public TempFolder() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }
}
