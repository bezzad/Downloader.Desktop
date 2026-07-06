using Downloader.Desktop.Plugins;
using Xunit;

namespace Downloader.Desktop.Plugins.Hls.Tests;

/// <summary>
/// Resolver-level tests for site extraction: <see cref="HlsResolver"/> with a stubbed <see cref="IYtDlp"/>
/// (canned JSON) and, for the HLS-reuse case, a <see cref="LoopbackServer"/> serving the extracted playlist.
/// </summary>
public class SiteExtractionResolverTests
{
    [Theory]
    [InlineData("https://x.com/user/status/123", true)]
    [InlineData("https://twitter.com/user/status/123", true)]
    [InlineData("https://www.twitter.com/user/status/123", true)]
    [InlineData("https://mobile.twitter.com/user/status/123", true)]
    [InlineData("https://youtube.com/watch?v=abc", true)]
    [InlineData("https://youtu.be/abc", true)]
    [InlineData("https://cdn.example.com/file.zip", false)]
    [InlineData("https://notx.com/status/1", false)]
    [InlineData("ftp://x.com/status/1", false)]
    [InlineData("", false)]
    public void CanResolve_claims_supported_site_hosts(string url, bool expected)
    {
        var resolver = new HlsResolver(); // no yt-dlp needed for the claim check
        Assert.Equal(expected, resolver.CanResolve(url));
    }

    [Fact]
    public async Task ResolveAsync_progressive_site_builds_single_combined_part()
    {
        var json = """
        {
          "title": "Tweet video",
          "http_headers": { "User-Agent": "yt-dlp/test" },
          "formats": [ { "format_id": "p", "url": "https://video.twimg.com/v.mp4", "ext": "mp4", "protocol": "https", "vcodec": "h264", "acodec": "aac", "height": 720, "filesize": 2048 } ]
        }
        """;
        var yt = new StubYtDlp(json);
        var resolver = new HlsResolver(ytDlp: yt);

        var plan = await resolver.ResolveAsync("https://x.com/u/status/1", CancellationToken.None);

        Assert.Equal(1, yt.Calls);
        Assert.Equal("Tweet video.mp4", plan.SuggestedFileName);
        Assert.Single(plan.Parts);
        Assert.Equal("https://video.twimg.com/v.mp4", plan.Parts[0].Url);
        Assert.Equal(PartKind.Combined, plan.Parts[0].Kind);
        Assert.Equal(2048, plan.Parts[0].ExpectedSize);
        Assert.Equal("yt-dlp/test", plan.Parts[0].Headers!["User-Agent"]);
        Assert.Equal(PostProcessKind.None, plan.PostProcess.Kind);
    }

    [Fact]
    public async Task ResolveAsync_video_plus_audio_site_builds_two_parts_with_mux()
    {
        var json = """
        {
          "title": "DASH video",
          "requested_formats": [
            { "format_id": "v", "url": "https://cdn/v.mp4", "protocol": "https", "vcodec": "vp9", "acodec": "none", "height": 1080 },
            { "format_id": "a", "url": "https://cdn/a.m4a", "protocol": "https", "vcodec": "none", "acodec": "opus" }
          ],
          "formats": [
            { "format_id": "v", "url": "https://cdn/v.mp4", "protocol": "https", "vcodec": "vp9", "acodec": "none", "height": 1080 },
            { "format_id": "a", "url": "https://cdn/a.m4a", "protocol": "https", "vcodec": "none", "acodec": "opus" }
          ]
        }
        """;
        var resolver = new HlsResolver(ytDlp: new StubYtDlp(json));

        var plan = await resolver.ResolveAsync("https://x.com/u/status/2", CancellationToken.None);

        Assert.Equal(2, plan.Parts.Count);
        Assert.Equal(PartKind.Video, plan.Parts[0].Kind);
        Assert.Equal("https://cdn/v.mp4", plan.Parts[0].Url);
        Assert.Equal(PartKind.Audio, plan.Parts[1].Kind);
        Assert.Equal("https://cdn/a.m4a", plan.Parts[1].Url);
        Assert.Equal(PostProcessKind.Mux, plan.PostProcess.Kind);
        Assert.Equal("DASH video.mp4", plan.SuggestedFileName);
    }

    [Fact]
    public async Task ResolveAsync_hls_site_reuses_segment_pipeline()
    {
        const string media =
            "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:0\n#EXTINF:6.0,\nseg0.ts\n#EXTINF:6.0,\nseg1.ts\n#EXT-X-ENDLIST\n";
        using var server = new LoopbackServer().MapText("/v/index.m3u8", media);

        // yt-dlp reports the best format is the (loopback) HLS playlist.
        var json = $$"""
        { "title": "HLS tweet", "formats": [ { "format_id": "hls", "url": "{{server.Url("v/index.m3u8")}}", "ext": "mp4", "protocol": "m3u8_native", "vcodec": "h264", "acodec": "aac", "height": 720 } ] }
        """;
        using var http = new HttpClient();
        var yt = new StubYtDlp(json);
        var resolver = new HlsResolver(http, ytDlp: yt);

        var plan = await resolver.ResolveAsync("https://x.com/u/status/3", CancellationToken.None);

        Assert.Equal(1, yt.Calls);
        Assert.Equal(2, plan.Parts.Count);
        Assert.Equal(server.Url("v/seg0.ts"), plan.Parts[0].Url);
        Assert.All(plan.Parts, p => Assert.Equal(PartKind.Segment, p.Kind));
        Assert.Equal(PostProcessKind.Concat, plan.PostProcess.Kind);
        Assert.Equal("HLS tweet.mp4", plan.SuggestedFileName);
    }

    [Fact]
    public async Task ResolveAsync_direct_m3u8_does_not_invoke_ytdlp()
    {
        const string media =
            "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:0\n#EXTINF:6.0,\nseg0.ts\n#EXTINF:6.0,\nseg1.ts\n#EXT-X-ENDLIST\n";
        using var server = new LoopbackServer().MapText("/direct/index.m3u8", media);
        using var http = new HttpClient();
        var yt = new StubYtDlp("{}");
        var resolver = new HlsResolver(http, ytDlp: yt);

        var plan = await resolver.ResolveAsync(server.Url("direct/index.m3u8"), CancellationToken.None);

        Assert.Equal(0, yt.Calls); // extraction bypassed for a direct playlist
        Assert.Equal(2, plan.Parts.Count);
        Assert.Equal(PostProcessKind.Concat, plan.PostProcess.Kind);
    }

    [Fact]
    public async Task ResolveAsync_no_media_throws_clear_error()
    {
        var resolver = new HlsResolver(ytDlp: new StubYtDlp("""{ "title": "x", "formats": [] }"""));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync("https://x.com/u/status/4", CancellationToken.None));
        Assert.Contains("No downloadable video", ex.Message);
    }

    [Fact]
    public async Task ResolveAsync_surfaces_provisioning_failure()
    {
        var resolver = new HlsResolver(ytDlp: new ThrowingYtDlp());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync("https://x.com/u/status/5", CancellationToken.None));
        Assert.Contains("unavailable", ex.Message);
    }

    private sealed class StubYtDlp(string json) : IYtDlp
    {
        public int Calls { get; private set; }
        public Task<string> ExtractJsonAsync(string url, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(json);
        }
    }

    private sealed class ThrowingYtDlp : IYtDlp
    {
        public Task<string> ExtractJsonAsync(string url, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Video extraction is unavailable: yt-dlp could not be downloaded.");
    }
}
