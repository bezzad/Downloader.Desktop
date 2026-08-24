using System.Text.Json;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Plugins.Hls;
using Downloader.Desktop.Plugins.Hls.Dash;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins.Hls;

/// <summary>
/// The DASH resolver: what it claims, the qualities it offers, and the shape of the plan it produces.
/// The plan's part ORDER and the recipe's stream grouping have to agree exactly — a mismatch is what makes
/// a download assemble into an unplayable file — so these are asserted together.
/// </summary>
public class DashResolverTests
{
    private static readonly Uri ManifestUri = new("https://host.example/path/manifest.mpd");

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("https://cdn.example.com/a/manifest.mpd", true)]
    [InlineData("https://cdn.example.com/a/manifest.MPD?token=abc&exp=1", true)]
    [InlineData("https://cdn.example.com/a/master.m3u8", false)]
    [InlineData("https://cdn.example.com/a/movie.mp4", false)]
    [InlineData("https://cdn.example.com/a/page", false)]
    [InlineData("", false)]
    public void CanResolve_claims_only_mpd_links(string url, bool expected) =>
        Assert.Equal(expected, new DashResolver().CanResolve(url));

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void It_is_not_a_fallback_resolver() =>
        Assert.False(((ILinkResolver)new DashResolver()).IsFallback);

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("https://h/x/movie.mpd", "movie.mp4")]
    [InlineData("https://h/x/movie.mpd?sig=1", "movie.mp4")]
    [InlineData("https://h/", "video.mp4")]
    public void SuggestFileName_uses_the_manifest_name_with_an_mp4_extension(string url, string expected) =>
        Assert.Equal(expected, DashResolver.SuggestFileName(url));

    // ── variants ────────────────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Variants_list_the_video_qualities_highest_first()
    {
        var resolver = FakeResolver("dash-timeline.mpd");

        var variants = await resolver.GetVariantsAsync(ManifestUri.ToString(), null, CancellationToken.None);

        Assert.NotNull(variants);
        Assert.Equal(2, variants!.Count);
        Assert.Equal("v1080", variants[0].Id);
        Assert.True(variants[0].IsDefault);
        Assert.StartsWith("1080p", variants[0].Label);
        Assert.Equal("v720", variants[1].Id);
        Assert.False(variants[1].IsDefault);

        // Size ≈ (video + audio bitrate) / 8 × duration: (4_800_000 + 128_000) / 8 × 100s.
        Assert.Equal((4_800_000L + 128_000L) / 8 * 100, variants[0].ExpectedSize);
        Assert.Contains("MB", variants[0].Label);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_manifest_with_one_quality_offers_no_choice()
    {
        var resolver = FakeResolver("dash-number.mpd");

        Assert.Null(await resolver.GetVariantsAsync(ManifestUri.ToString(), null, CancellationToken.None));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_link_this_resolver_does_not_claim_has_no_variants()
    {
        var resolver = FakeResolver("dash-timeline.mpd");

        Assert.Null(await resolver.GetVariantsAsync("https://h/master.m3u8", null, CancellationToken.None));
    }

    // ── plan ────────────────────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_plan_is_video_parts_then_audio_parts_with_a_matching_two_stream_recipe()
    {
        var resolver = FakeResolver("dash-timeline.mpd");

        var plan = await resolver.ResolveAsync(ManifestUri.ToString(), new ResolveOptions(), CancellationToken.None);

        Assert.Equal("manifest.mp4", plan.SuggestedFileName);
        // Default pick is the highest bitrate: v1080 init + 4 segments, then a128 init + 4 segments.
        Assert.Equal(10, plan.Parts.Count);
        Assert.Equal("https://cdn.example.com/vod/v1080/init.mp4", plan.Parts[0].Url);
        Assert.Equal("https://cdn.example.com/vod/v1080/seg-12000.m4s", plan.Parts[4].Url);
        Assert.Equal("https://cdn.example.com/vod/a128/init.mp4", plan.Parts[5].Url);
        Assert.All(plan.Parts, p => Assert.Equal(PartKind.Segment, p.Kind));

        var recipe = Recipe(plan);
        Assert.Equal(".mp4", recipe.IntermediateExtension);
        Assert.Equal(2, recipe.Streams!.Count);
        Assert.All(recipe.Streams, g => Assert.True(g.HasInitSegment));
        Assert.Equal(4, recipe.Streams[0].SegmentCount);
        Assert.Equal(4, recipe.Streams[1].SegmentCount);
        // The invariant that matters: the recipe accounts for exactly the parts the plan downloads.
        Assert.Equal(plan.Parts.Count, recipe.Streams.Sum(g => g.FileCount));
        Assert.Equal(recipe.Streams.Sum(g => g.SegmentCount), recipe.Segments.Count);
        Assert.All(recipe.Segments, s => Assert.False(s.Encrypted));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_chosen_variant_is_downloaded()
    {
        var resolver = FakeResolver("dash-timeline.mpd");

        var plan = await resolver.ResolveAsync(
            ManifestUri.ToString(), new ResolveOptions { VariantId = "v720" }, CancellationToken.None);

        Assert.Equal("https://cdn.example.com/vod/v720/init.mp4", plan.Parts[0].Url);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_unknown_variant_falls_back_to_the_best_one()
    {
        var resolver = FakeResolver("dash-timeline.mpd");

        var plan = await resolver.ResolveAsync(
            ManifestUri.ToString(), new ResolveOptions { VariantId = "no-such-id" }, CancellationToken.None);

        Assert.Equal("https://cdn.example.com/vod/v1080/init.mp4", plan.Parts[0].Url);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Whole_file_representations_become_ordinary_video_and_audio_parts()
    {
        var resolver = FakeResolver("dash-segmentbase.mpd");

        var plan = await resolver.ResolveAsync(ManifestUri.ToString(), new ResolveOptions(), CancellationToken.None);

        Assert.Equal(2, plan.Parts.Count);
        // Not Segment: these are whole files, so the engine should chunk them in parallel as usual.
        Assert.Equal(PartKind.Video, plan.Parts[0].Kind);
        Assert.Equal(PartKind.Audio, plan.Parts[1].Kind);
        Assert.Equal("https://cdn.example.com/od/movie-video.mp4", plan.Parts[0].Url);
        Assert.Equal("https://cdn.example.com/od/movie-audio.mp4", plan.Parts[1].Url);

        var recipe = Recipe(plan);
        Assert.All(recipe.Streams!, g => Assert.False(g.HasInitSegment));
        Assert.All(recipe.Streams!, g => Assert.Equal(1, g.SegmentCount));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Request_headers_are_stamped_onto_every_part()
    {
        var resolver = FakeResolver("dash-timeline.mpd");
        var headers = new Dictionary<string, string> { ["Referer"] = "https://site.example/watch" };

        var plan = await resolver.ResolveAsync(
            ManifestUri.ToString(), new ResolveOptions { Headers = headers }, CancellationToken.None);

        Assert.All(plan.Parts, p =>
            Assert.Equal("https://site.example/watch", p.Headers!["Referer"]));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_refused_manifest_surfaces_its_reason()
    {
        var resolver = FakeResolver("dash-live.mpd");

        var ex = await Assert.ThrowsAsync<DashException>(() =>
            resolver.ResolveAsync(ManifestUri.ToString(), new ResolveOptions(), CancellationToken.None));
        Assert.Contains("live", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── over real HTTP ──────────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = TestTimeouts.SlowMs)]
    public async Task Resolves_a_manifest_served_over_http()
    {
        using var server = new LoopbackServer();
        server.MapText("/stream/manifest.mpd", TestFixtures.Read("dash-number.mpd"), "application/dash+xml");
        var resolver = new DashResolver(new HttpClient());

        var plan = await resolver.ResolveAsync(
            server.Url("stream/manifest.mpd"), new ResolveOptions(), CancellationToken.None);

        // Relative template URLs resolve against the manifest's own address.
        Assert.Equal(10, plan.Parts.Count); // (1 init + 4 segments) × 2 streams
        Assert.Equal(server.Url("stream/video/1500000/init.mp4"), plan.Parts[0].Url);
        Assert.Equal(server.Url("stream/video/1500000/chunk-0001.m4s"), plan.Parts[1].Url);
        Assert.Equal(server.Url("stream/audio/init.mp4"), plan.Parts[5].Url);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A resolver whose HTTP layer is replaced by a fixed fixture, so these tests never touch the
    /// network and still exercise the real parser.</summary>
    private static DashResolver FakeResolver(string fixture) =>
        new(new HttpClient(new FixtureHandler(TestFixtures.Read(fixture), ManifestUri)));

    private static ConcatRecipe Recipe(DownloadPlan plan)
    {
        Assert.Equal(PostProcessKind.Concat, plan.PostProcess.Kind);
        return JsonSerializer.Deserialize<ConcatRecipe>(plan.PostProcess.Recipe!)!;
    }

    private sealed class FixtureHandler(string content, Uri uri) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(content),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, uri),
            });
    }
}
