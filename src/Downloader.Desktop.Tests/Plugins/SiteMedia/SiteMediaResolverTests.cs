using Downloader.Desktop.Plugins;
using Downloader.Desktop.Plugins.SiteMedia;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins.SiteMedia;

/// <summary>
/// The site-media plugin's resolution policy, driven entirely by recorded extraction output: which pages it
/// claims, what parts a page becomes, which qualities it offers, and what a page it cannot extract says.
/// No network, no extraction binary, no site — the tool is stubbed, which is the whole reason
/// <see cref="IYtDlp"/> exists.
/// </summary>
public class SiteMediaResolverTests
{
    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("https://www.youtube.com/watch?v=abc", true)]
    [InlineData("https://youtu.be/abc", true)]
    [InlineData("https://m.youtube.com/watch?v=abc", true)]
    [InlineData("https://x.com/user/status/123", true)]
    [InlineData("https://mobile.twitter.com/user/status/123", true)]
    [InlineData("https://vm.tiktok.com/ZM123/", true)]
    [InlineData("https://cdn.example.com/file.zip", false)]
    [InlineData("https://notyoutube.com/watch?v=abc", false)]
    [InlineData("https://youtube.com.evil.example/watch?v=abc", false)]
    [InlineData("ftp://x.com/status/1", false)]
    [InlineData("", false)]
    public void CanResolve_claims_supported_site_pages_only(string url, bool expected)
    {
        var resolver = NewResolver(new StubYtDlp("{}"));
        Assert.Equal(expected, resolver.CanResolve(url));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void CanResolve_makes_no_network_call()
    {
        var yt = new StubYtDlp("{}");
        var resolver = NewResolver(yt);

        for (var i = 0; i < 50; i++)
            resolver.CanResolve("https://www.youtube.com/watch?v=abc");

        Assert.Equal(0, yt.Calls); // the Add window asks this on every keystroke
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_progressive_page_becomes_one_part_named_after_the_page()
    {
        var resolver = NewResolver(new StubYtDlp(ProgressiveJson));

        var plan = await resolver.ResolveAsync("https://x.com/u/status/1", TestContext.Current.CancellationToken);

        Assert.Equal("Tweet video.mp4", plan.SuggestedFileName);
        Assert.Single(plan.Parts);
        Assert.Equal("https://video.twimg.com/v.mp4", plan.Parts[0].Url);
        Assert.Equal(PartKind.Combined, plan.Parts[0].Kind);
        Assert.Equal(2048, plan.Parts[0].ExpectedSize);
        Assert.Equal("yt-dlp/test", plan.Parts[0].Headers!["User-Agent"]);
        Assert.Equal(PostProcessKind.None, plan.PostProcess.Kind);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_page_with_separate_streams_becomes_a_video_audio_pair_to_mux()
    {
        var resolver = NewResolver(new StubYtDlp(SplitStreamsJson));

        var plan = await resolver.ResolveAsync("https://www.youtube.com/watch?v=abc",
            TestContext.Current.CancellationToken);

        Assert.Equal(2, plan.Parts.Count);
        // Order matters: MuxPostProcessor hands input[0] to ffmpeg as the video.
        Assert.Equal(PartKind.Video, plan.Parts[0].Kind);
        Assert.Equal("https://cdn/v1080.mp4", plan.Parts[0].Url);
        Assert.Equal(PartKind.Audio, plan.Parts[1].Kind);
        Assert.Equal("https://cdn/a.m4a", plan.Parts[1].Url);
        Assert.Equal(PostProcessKind.Mux, plan.PostProcess.Kind);
        Assert.Equal("A talk.mp4", plan.SuggestedFileName);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_page_with_no_media_says_so_instead_of_failing_as_a_network_error()
    {
        var resolver = NewResolver(new StubYtDlp("""{ "title": "x", "formats": [] }"""));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync("https://x.com/u/status/4", TestContext.Current.CancellationToken));

        Assert.Contains("No downloadable video", ex.Message);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_adaptive_only_page_names_the_reason_and_the_plugin_that_handles_it()
    {
        var json = """
        { "title": "Live-ish", "formats": [
          { "format_id": "hls", "url": "https://cdn/index.m3u8", "protocol": "m3u8_native", "vcodec": "h264", "acodec": "aac", "height": 720 } ] }
        """;
        var resolver = NewResolver(new StubYtDlp(json));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync("https://vimeo.com/123", TestContext.Current.CancellationToken));

        Assert.Equal(SiteMediaResolver.AdaptiveOnlyMessage, ex.Message);
        Assert.Contains("adaptive stream", ex.Message);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_failure_from_the_extraction_tool_reaches_the_user_unchanged()
    {
        var resolver = NewResolver(new ThrowingYtDlp("Video extraction is unavailable: yt-dlp could not be downloaded."));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync("https://x.com/u/status/5", TestContext.Current.CancellationToken));

        Assert.Contains("unavailable", ex.Message);
    }

    // ── Variants (per-quality choices) ────────────────────────────────────────────────────────────────

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Several_qualities_are_offered_as_several_variants_best_first()
    {
        var resolver = NewResolver(new StubYtDlp(SplitStreamsJson));

        var variants = await resolver.GetVariantsAsync("https://www.youtube.com/watch?v=abc", null,
            TestContext.Current.CancellationToken);

        Assert.NotNull(variants);
        Assert.Equal(new[] { "1080", "360", "audio" }, variants!.Select(v => v.Id).ToArray());
        Assert.True(variants[0].IsDefault); // tallest wins by default
        Assert.All(variants, v => Assert.False(string.IsNullOrWhiteSpace(v.Label)));
        Assert.Contains("1080p", variants[0].Label);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_chosen_variant_is_what_gets_downloaded()
    {
        var resolver = NewResolver(new StubYtDlp(SplitStreamsJson));
        var ct = TestContext.Current.CancellationToken;

        var pinned = await resolver.ResolveAsync("https://www.youtube.com/watch?v=abc",
            new ResolveOptions { VariantId = "360" }, ct);
        Assert.Single(pinned.Parts);
        Assert.Equal("https://cdn/v360.mp4", pinned.Parts[0].Url); // the 360p progressive, not the 1080p pair

        var audio = await resolver.ResolveAsync("https://www.youtube.com/watch?v=abc",
            new ResolveOptions { VariantId = "audio" }, ct);
        Assert.Single(audio.Parts);
        Assert.Equal("https://cdn/a.m4a", audio.Parts[0].Url);
        Assert.Equal("A talk.m4a", audio.SuggestedFileName);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_page_offering_one_quality_asks_nothing()
    {
        var resolver = NewResolver(new StubYtDlp(ProgressiveJson));

        var variants = await resolver.GetVariantsAsync("https://x.com/u/status/1", null,
            TestContext.Current.CancellationToken);

        Assert.Null(variants); // no real choice ⇒ no picker
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Listing_qualities_then_downloading_extracts_only_once()
    {
        var yt = new StubYtDlp(SplitStreamsJson);
        var resolver = NewResolver(yt);
        var ct = TestContext.Current.CancellationToken;

        await resolver.GetVariantsAsync("https://www.youtube.com/watch?v=abc", null, ct);
        await resolver.ResolveAsync("https://www.youtube.com/watch?v=abc", new ResolveOptions { VariantId = "1080" }, ct);

        // Extraction takes 5-20 s; the Add flow must not pay for it twice.
        Assert.Equal(1, yt.Calls);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────────────────────────────

    private const string ProgressiveJson = """
    {
      "title": "Tweet video",
      "http_headers": { "User-Agent": "yt-dlp/test" },
      "formats": [ { "format_id": "p", "url": "https://video.twimg.com/v.mp4", "ext": "mp4", "protocol": "https", "vcodec": "h264", "acodec": "aac", "height": 720, "filesize": 2048 } ]
    }
    """;

    /// <summary>A YouTube-shaped result: one low progressive format plus taller video-only streams with a
    /// separate audio stream — the case where preferring "simple" would silently cap quality at 360p.</summary>
    private const string SplitStreamsJson = """
    {
      "title": "A talk",
      "formats": [
        { "format_id": "18", "url": "https://cdn/v360.mp4", "ext": "mp4", "protocol": "https", "vcodec": "h264", "acodec": "aac", "height": 360, "filesize": 1000 },
        { "format_id": "137", "url": "https://cdn/v1080.mp4", "ext": "mp4", "protocol": "https", "vcodec": "h264", "acodec": "none", "height": 1080, "filesize": 8000 },
        { "format_id": "140", "url": "https://cdn/a.m4a", "ext": "m4a", "protocol": "https", "vcodec": "none", "acodec": "aac", "tbr": 128, "filesize": 500 }
      ]
    }
    """;

    // The same page as SplitStreamsJson, extracted through a different player client: same video, links
    // the CDN actually serves.
    private const string RetriedSplitStreamsJson = """
    {
      "title": "A talk",
      "formats": [
        { "format_id": "137", "url": "https://cdn/retried-v1080.mp4", "ext": "mp4", "protocol": "https", "vcodec": "h264", "acodec": "none", "height": 1080, "filesize": 8000 },
        { "format_id": "140", "url": "https://cdn/retried-a.m4a", "ext": "m4a", "protocol": "https", "vcodec": "none", "acodec": "aac", "tbr": 128, "filesize": 500 }
      ]
    }
    """;

    // --- The refused-link retry (issue: YouTube pages failing with "403 (Forbidden)") ---
    //
    // YouTube answers an extraction through one of several internal player clients, and its CDN then
    // refuses SOME of those clients' links (the reported case came back through WEB_EMBEDDED_PLAYER,
    // whose links need a token this plugin cannot mint). The extraction looked perfectly successful, so
    // the app planned a download around a link that 403'd on its very first request.

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_refused_youtube_link_is_re_extracted_through_another_player_client()
    {
        var yt = new ClientAwareYtDlp(defaultJson: SplitStreamsJson, clientJson: RetriedSplitStreamsJson);
        var probe = new StubProbe(ProbeVerdict.Refused, ProbeVerdict.Ok);
        var resolver = NewResolver(yt, probe);

        var plan = await resolver.ResolveAsync("https://www.youtube.com/watch?v=abc", null,
            TestContext.Current.CancellationToken);

        Assert.Equal("https://cdn/retried-v1080.mp4", plan.Parts[0].Url);
        Assert.Equal("tv_simply", yt.LastClient);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_first_retry_drops_the_session_that_made_youtube_gate_the_links()
    {
        var yt = new ClientAwareYtDlp(defaultJson: SplitStreamsJson, clientJson: RetriedSplitStreamsJson);
        var resolver = NewResolver(yt, new StubProbe(ProbeVerdict.Refused, ProbeVerdict.Ok));

        var plan = await resolver.ResolveAsync("https://www.youtube.com/watch?v=abc",
            new ResolveOptions { CookieFilePath = "/tmp/cookies.txt" }, TestContext.Current.CancellationToken);

        // Handing yt-dlp a signed-in session is what makes YouTube answer through the clients whose links
        // its CDN then refuses without a token we cannot mint, so the cheapest thing to try is the same
        // page with no session at all.
        Assert.Null(yt.LastCookieFile);
        Assert.Null(yt.LastClient);
        Assert.Equal(0, yt.ClientCalls);
        Assert.Equal("https://cdn/v1080.mp4", plan.Parts[0].Url);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Every_client_refused_says_so_instead_of_planning_a_download_that_cannot_start()
    {
        var yt = new ClientAwareYtDlp(defaultJson: SplitStreamsJson, clientJson: RetriedSplitStreamsJson);
        var resolver = NewResolver(yt, new StubProbe(ProbeVerdict.Refused));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync("https://www.youtube.com/watch?v=abc", null,
                TestContext.Current.CancellationToken));

        Assert.Equal(SiteMediaResolver.AllRefusedMessage, ex.Message);
        // Every pinned client was tried. The drop-the-session entry is skipped here: this add carried no
        // session, so the first attempt already was the anonymous one.
        Assert.Equal(SiteMediaResolver.YouTubeRetryClients.Count(c => c != null), yt.ClientCalls);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_probe_that_cannot_reach_the_server_never_rejects_a_link()
    {
        var yt = new ClientAwareYtDlp(defaultJson: SplitStreamsJson, clientJson: RetriedSplitStreamsJson);
        var resolver = NewResolver(yt, new StubProbe(ProbeVerdict.Unknown));

        var plan = await resolver.ResolveAsync("https://www.youtube.com/watch?v=abc", null,
            TestContext.Current.CancellationToken);

        Assert.Equal("https://cdn/v1080.mp4", plan.Parts[0].Url); // the original choice stands
        Assert.Equal(0, yt.ClientCalls);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_site_with_no_alternative_extraction_is_never_probed()
    {
        var probe = new StubProbe(ProbeVerdict.Refused);
        var resolver = NewResolver(new StubYtDlp(ProgressiveJson), probe);

        // Only YouTube offers another client to ask; probing anywhere else would spend a request on a
        // question nothing can act on.
        var plan = await resolver.ResolveAsync("https://x.com/u/status/1", null,
            TestContext.Current.CancellationToken);

        Assert.Equal("https://video.twimg.com/v.mp4", plan.Parts[0].Url);
        Assert.Empty(probe.Urls);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_retry_keeps_the_session_the_link_was_sent_with()
    {
        var yt = new ClientAwareYtDlp(defaultJson: SplitStreamsJson, clientJson: RetriedSplitStreamsJson);
        // Refused, refused without the session too, then served by the first pinned client.
        var resolver = NewResolver(yt, new StubProbe(ProbeVerdict.Refused, ProbeVerdict.Refused, ProbeVerdict.Ok));

        await resolver.ResolveAsync("https://www.youtube.com/watch?v=abc",
            new ResolveOptions { CookieFilePath = "/tmp/cookies.txt" }, TestContext.Current.CancellationToken);

        // A signed-in-only page is exactly the case that gets this far — a pinned-client retry that
        // dropped the cookies would just walk into a bot check.
        Assert.Equal("/tmp/cookies.txt", yt.LastCookieFile);
        Assert.Equal("tv_simply", yt.LastClient);
    }

    /// <summary>Serves one extraction by default and a different one when a player client is pinned.</summary>
    internal sealed class ClientAwareYtDlp(string defaultJson, string clientJson) : IYtDlp
    {
        public string? LastClient { get; private set; }
        public string? LastCookieFile { get; private set; }
        public int ClientCalls { get; private set; }

        public Task<string> ExtractJsonAsync(string url, CancellationToken cancellationToken)
            => Task.FromResult(defaultJson);

        public Task<string> ExtractJsonAsync(string url, string? cookieFilePath, CancellationToken cancellationToken)
        {
            LastCookieFile = cookieFilePath;
            return Task.FromResult(defaultJson);
        }

        public Task<string> ExtractJsonAsync(
            string url, string? cookieFilePath, string? playerClient, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(playerClient))
                return ExtractJsonAsync(url, cookieFilePath, cancellationToken);
            ClientCalls++;
            LastClient = playerClient;
            LastCookieFile = cookieFilePath;
            return Task.FromResult(clientJson);
        }
    }

    /// <summary>A resolver wired to a probe that says every extracted link is fetchable — the normal
    /// case, and the only way these tests stay network-free now that a YouTube resolve checks its chosen
    /// stream before planning a download around it.</summary>
    internal static SiteMediaResolver NewResolver(IYtDlp yt, IMediaProbe? probe = null)
        => new(yt, null, probe ?? new StubProbe(ProbeVerdict.Ok));

    /// <summary>Answers a scripted sequence of verdicts (the last one repeats) and records what it was
    /// asked about.</summary>
    internal sealed class StubProbe(params ProbeVerdict[] verdicts) : IMediaProbe
    {
        private int _calls;
        public List<string> Urls { get; } = new();

        public Task<ProbeVerdict> CheckAsync(
            string url, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
        {
            Urls.Add(url);
            var verdict = verdicts[Math.Min(_calls, verdicts.Length - 1)];
            _calls++;
            return Task.FromResult(verdict);
        }
    }

    internal sealed class StubYtDlp(string json) : IYtDlp
    {
        public int Calls { get; private set; }
        public string? LastCookieFile { get; private set; }

        public Task<string> ExtractJsonAsync(string url, CancellationToken cancellationToken)
            => ExtractJsonAsync(url, null, cancellationToken);

        public Task<string> ExtractJsonAsync(string url, string? cookieFilePath, CancellationToken cancellationToken)
        {
            Calls++;
            LastCookieFile = cookieFilePath;
            return Task.FromResult(json);
        }
    }

    private sealed class ThrowingYtDlp(string message) : IYtDlp
    {
        public Task<string> ExtractJsonAsync(string url, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(message);
    }
}
