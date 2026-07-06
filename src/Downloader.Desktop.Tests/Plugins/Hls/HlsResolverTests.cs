using System.Text.Json;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Plugins.Hls;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins.Hls;

public class HlsResolverTests
{
    [Theory]
    [InlineData("https://cdn.example.com/v/index.m3u8", true)]
    [InlineData("https://cdn.example.com/v/index.M3U8?token=abc&x=1", true)]
    [InlineData("https://cdn.example.com/v/playlist.m3u", true)]
    [InlineData("https://cdn.example.com/file.zip", false)]
    [InlineData("https://cdn.example.com/video.mp4", false)]
    [InlineData("", false)]
    public void CanResolve_detects_by_url(string url, bool expected)
    {
        var resolver = new HlsResolver();
        Assert.Equal(expected, resolver.CanResolve(url));
    }

    [Fact]
    public void CanResolve_uses_content_type_probe_when_url_is_ambiguous()
    {
        var resolver = new HlsResolver(probe: new FakeProbe(isHls: true));
        Assert.True(resolver.CanResolve("https://cdn.example.com/stream?id=42"));

        var noProbe = new HlsResolver();
        Assert.False(noProbe.CanResolve("https://cdn.example.com/stream?id=42"));
    }

    [Fact]
    public async Task ResolveAsync_builds_plan_from_media_playlist()
    {
        const string media =
            "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:0\n" +
            "#EXTINF:9.0,\nseg0.ts\n#EXTINF:9.0,\nseg1.ts\n#EXTINF:3.0,\nseg2.ts\n#EXT-X-ENDLIST\n";
        using var server = new LoopbackServer().MapText("/video/index.m3u8", media);
        using var http = new HttpClient();
        var resolver = new HlsResolver(http);

        var plan = await resolver.ResolveAsync(server.Url("video/index.m3u8"), CancellationToken.None);

        Assert.Equal(3, plan.Parts.Count);
        Assert.Equal(server.Url("video/seg0.ts"), plan.Parts[0].Url);
        Assert.Equal(server.Url("video/seg1.ts"), plan.Parts[1].Url);
        Assert.Equal(server.Url("video/seg2.ts"), plan.Parts[2].Url);
        Assert.All(plan.Parts, p => Assert.Equal(PartKind.Segment, p.Kind));
        Assert.Equal("index.mp4", plan.SuggestedFileName);

        Assert.Equal(PostProcessKind.Concat, plan.PostProcess.Kind);
        var recipe = JsonSerializer.Deserialize<ConcatRecipe>(plan.PostProcess.Recipe!)!;
        Assert.False(recipe.HasInitSegment);
        Assert.Equal(3, recipe.Segments.Count);
        Assert.All(recipe.Segments, s => Assert.False(s.Encrypted));
    }

    [Fact]
    public async Task ResolveAsync_follows_master_to_best_variant()
    {
        const string master =
            "#EXTM3U\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=800000\nlow/index.m3u8\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=4800000\nhigh/index.m3u8\n";
        const string high =
            "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:0\n#EXTINF:6.0,\na.ts\n#EXTINF:6.0,\nb.ts\n#EXT-X-ENDLIST\n";
        using var server = new LoopbackServer()
            .MapText("/master.m3u8", master)
            .MapText("/high/index.m3u8", high);
        using var http = new HttpClient();
        var resolver = new HlsResolver(http);

        var plan = await resolver.ResolveAsync(server.Url("master.m3u8"), CancellationToken.None);

        Assert.Equal(2, plan.Parts.Count);
        Assert.Equal(server.Url("high/a.ts"), plan.Parts[0].Url);
        Assert.Equal(server.Url("high/b.ts"), plan.Parts[1].Url);
    }

    [Fact]
    public async Task ResolveAsync_carries_aes_key_and_iv_and_init_segment()
    {
        const string media =
            "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:0\n" +
            "#EXT-X-MAP:URI=\"init.mp4\"\n" +
            "#EXT-X-KEY:METHOD=AES-128,URI=\"k.bin\",IV=0x000102030405060708090A0B0C0D0E0F\n" +
            "#EXTINF:6.0,\nseg0.ts\n#EXTINF:6.0,\nseg1.ts\n#EXT-X-ENDLIST\n";
        using var server = new LoopbackServer().MapText("/enc/index.m3u8", media);
        using var http = new HttpClient();
        var resolver = new HlsResolver(http);

        var plan = await resolver.ResolveAsync(server.Url("enc/index.m3u8"), CancellationToken.None);

        // init segment is the first part, then the two media segments
        Assert.Equal(3, plan.Parts.Count);
        Assert.Equal(server.Url("enc/init.mp4"), plan.Parts[0].Url);
        Assert.Equal(server.Url("enc/seg0.ts"), plan.Parts[1].Url);

        var recipe = JsonSerializer.Deserialize<ConcatRecipe>(plan.PostProcess.Recipe!)!;
        Assert.True(recipe.HasInitSegment);
        Assert.Equal(2, recipe.Segments.Count);
        Assert.True(recipe.Segments[0].Encrypted);
        Assert.Equal(server.Url("enc/k.bin"), recipe.Segments[0].KeyUri);
        Assert.Equal("000102030405060708090A0B0C0D0E0F", recipe.Segments[0].IvHex);
    }

    private sealed class FakeProbe(bool isHls) : IContentTypeProbe
    {
        public bool LooksLikeHls(string url) => isHls;
    }
}
