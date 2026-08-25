using System.Text.Json;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Plugins.Hls;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins.Hls;

public class HlsResolverTests
{
    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("https://cdn.example.com/v/index.m3u8", true)]
    [InlineData("https://cdn.example.com/v/index.M3U8?token=abc&x=1", true)]
    [InlineData("https://cdn.example.com/v/playlist.m3u", true)]
    [InlineData("https://cdn.example.com/file.zip", false)]
    [InlineData("https://cdn.example.com/video.mp4", false)]
    [InlineData("https://youtube.com/watch?v=abc", false)]
    [InlineData("https://youtu.be/abc", false)]
    [InlineData("https://x.com/user/status/123", false)]
    [InlineData("", false)]
    public void CanResolve_detects_by_url(string url, bool expected)
    {
        var resolver = new HlsResolver();
        Assert.Equal(expected, resolver.CanResolve(url));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void CanResolve_uses_content_type_probe_when_url_is_ambiguous()
    {
        var resolver = new HlsResolver(probe: new FakeProbe(isHls: true));
        Assert.True(resolver.CanResolve("https://cdn.example.com/stream?id=42"));

        var noProbe = new HlsResolver();
        Assert.False(noProbe.CanResolve("https://cdn.example.com/stream?id=42"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
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

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task ResolveAsync_stamps_the_supplied_headers_onto_every_part()
    {
        // Issue #7: a protected stream is only served with the context it was found in, so the headers the
        // host was given must reach the segment requests too — not just the playlist fetch.
        const string media =
            "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:0\n#EXTINF:9.0,\nseg0.ts\n#EXTINF:9.0,\nseg1.ts\n#EXT-X-ENDLIST\n";
        using var server = new LoopbackServer().MapText("/video/index.m3u8", media);
        using var http = new HttpClient();
        var resolver = new HlsResolver(http);
        var options = new ResolveOptions
        {
            Headers = new Dictionary<string, string>
            {
                ["Referer"] = "https://site.example/watch/42",
                ["Origin"] = "https://site.example"
            }
        };

        var plan = await resolver.ResolveAsync(server.Url("video/index.m3u8"), options, CancellationToken.None);

        Assert.Equal(2, plan.Parts.Count);
        Assert.All(plan.Parts, p =>
        {
            Assert.Equal("https://site.example/watch/42", p.Headers!["Referer"]);
            Assert.Equal("https://site.example", p.Headers["Origin"]);
        });
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
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

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task GetVariantsAsync_lists_master_qualities_highest_default_with_size()
    {
        const string master =
            "#EXTM3U\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360\nlow/index.m3u8\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=2400000,RESOLUTION=1280x720\nmid/index.m3u8\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=4800000,RESOLUTION=1920x1080\nhigh/index.m3u8\n";
        // 2 × 6 s = 12 s → size = bandwidth/8 × duration
        const string high =
            "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:0\n#EXTINF:6.0,\na.ts\n#EXTINF:6.0,\nb.ts\n#EXT-X-ENDLIST\n";
        using var server = new LoopbackServer()
            .MapText("/master.m3u8", master)
            .MapText("/high/index.m3u8", high);
        using var http = new HttpClient();
        var resolver = new HlsResolver(http);

        var variants = await resolver.GetVariantsAsync(server.Url("master.m3u8"), null, CancellationToken.None);

        Assert.NotNull(variants);
        Assert.Equal(new[] { "4800000", "2400000", "800000" }, variants!.Select(v => v.Id));
        Assert.True(variants[0].IsDefault);
        Assert.All(variants.Skip(1), v => Assert.False(v.IsDefault));
        Assert.Equal("1080p (≈7 MB)", variants[0].Label); // 4_800_000/8 * 12 = 7_200_000
        Assert.Equal("720p (≈3 MB)", variants[1].Label);  // 2_400_000/8 * 12 = 3_600_000
        Assert.Equal("360p (≈1 MB)", variants[2].Label);  //   800_000/8 * 12 = 1_200_000
        Assert.Equal(7_200_000, variants[0].ExpectedSize);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task GetVariantsAsync_media_playlist_offers_no_picker()
    {
        const string media =
            "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:0\n#EXTINF:6.0,\na.ts\n#EXT-X-ENDLIST\n";
        using var server = new LoopbackServer().MapText("/video/index.m3u8", media);
        using var http = new HttpClient();
        var resolver = new HlsResolver(http);

        Assert.Null(await resolver.GetVariantsAsync(server.Url("video/index.m3u8"), null, CancellationToken.None));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task ResolveAsync_honors_variant_id_instead_of_best()
    {
        const string master =
            "#EXTM3U\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360\nlow/index.m3u8\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=4800000,RESOLUTION=1920x1080\nhigh/index.m3u8\n";
        const string low =
            "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:0\n#EXTINF:6.0,\nlo.ts\n#EXT-X-ENDLIST\n";
        const string high =
            "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:0\n#EXTINF:6.0,\nhi.ts\n#EXT-X-ENDLIST\n";
        using var server = new LoopbackServer()
            .MapText("/master.m3u8", master)
            .MapText("/low/index.m3u8", low)
            .MapText("/high/index.m3u8", high);
        using var http = new HttpClient();
        var resolver = new HlsResolver(http);

        var plan = await resolver.ResolveAsync(
            server.Url("master.m3u8"),
            new ResolveOptions { VariantId = "800000" },
            CancellationToken.None);

        Assert.Single(plan.Parts);
        Assert.Equal(server.Url("low/lo.ts"), plan.Parts[0].Url);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task GetVariantsAsync_then_resolve_reuses_cached_playlists()
    {
        const string master =
            "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1000000,RESOLUTION=1280x720\nmid/index.m3u8\n";
        const string mid =
            "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:0\n#EXTINF:4.0,\na.ts\n#EXT-X-ENDLIST\n";
        using var server = new LoopbackServer()
            .MapText("/master.m3u8", master)
            .MapText("/mid/index.m3u8", mid);
        using var http = new HttpClient();
        var resolver = new HlsResolver(http);
        var url = server.Url("master.m3u8");

        var variants = await resolver.GetVariantsAsync(url, null, CancellationToken.None);
        Assert.Equal("1000000", Assert.Single(variants!).Id);

        var plan = await resolver.ResolveAsync(url, new ResolveOptions { VariantId = "1000000" }, CancellationToken.None);
        Assert.Equal(server.Url("mid/a.ts"), Assert.Single(plan.Parts).Url);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
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

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_encrypted_plan_carries_the_request_headers_for_the_key_fetch()
    {
        // The key is fetched later, by the post-processor, from a client that knows nothing about this
        // download — so the context has to ride on the recipe or that one request goes out anonymous.
        const string media =
            "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:0\n" +
            "#EXT-X-KEY:METHOD=AES-128,URI=\"k.bin\",IV=0x000102030405060708090A0B0C0D0E0F\n" +
            "#EXTINF:6.0,\nseg0.ts\n#EXT-X-ENDLIST\n";
        using var server = new LoopbackServer().MapText("/enc/index.m3u8", media);
        using var http = new HttpClient();
        var resolver = new HlsResolver(http);
        var headers = new Dictionary<string, string>
        {
            ["Cookie"] = "SID=abc",
            ["Referer"] = "https://site.example/watch",
        };

        var plan = await resolver.ResolveAsync(
            server.Url("enc/index.m3u8"), new ResolveOptions { Headers = headers }, CancellationToken.None);

        var recipe = JsonSerializer.Deserialize<ConcatRecipe>(plan.PostProcess.Recipe!)!;
        Assert.Equal("SID=abc", recipe.KeyHeaders!["Cookie"]);
        Assert.Equal("https://site.example/watch", recipe.KeyHeaders["Referer"]);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_unencrypted_plan_carries_no_key_headers()
    {
        // Nothing to fetch a key for, so nothing to carry — and a recipe with no KeyHeaders is exactly what
        // every recipe written before this change looks like.
        const string media = "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:0\n#EXTINF:6.0,\nseg0.ts\n#EXT-X-ENDLIST\n";
        using var server = new LoopbackServer().MapText("/plain/index.m3u8", media);
        using var http = new HttpClient();
        var resolver = new HlsResolver(http);

        var plan = await resolver.ResolveAsync(
            server.Url("plain/index.m3u8"),
            new ResolveOptions { Headers = new Dictionary<string, string> { ["Cookie"] = "SID=abc" } },
            CancellationToken.None);

        Assert.Null(JsonSerializer.Deserialize<ConcatRecipe>(plan.PostProcess.Recipe!)!.KeyHeaders);
    }

    private sealed class FakeProbe(bool isHls) : IContentTypeProbe
    {
        public bool LooksLikeHls(string url) => isHls;
    }
}
