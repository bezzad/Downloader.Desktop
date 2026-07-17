using Downloader.Desktop.Plugins.Hls;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins.Hls;

/// <summary>
/// Unit tests for the pure format-selection core: canned <c>yt-dlp -J</c> JSON → the chosen
/// <see cref="ExtractionResult"/>. No network, no real binary (internals are visible to this test project).
/// </summary>
public class SiteExtractorTests
{
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Select_prefers_progressive_mp4()
    {
        const string json = """
        {
          "title": "Cool clip",
          "id": "123",
          "http_headers": { "User-Agent": "yt-dlp/test", "Referer": "https://x.com/" },
          "formats": [
            { "format_id": "a", "url": "https://cdn/a.m4a", "ext": "m4a", "protocol": "https", "vcodec": "none", "acodec": "mp4a.40.2", "tbr": 128 },
            { "format_id": "p", "url": "https://cdn/video.mp4", "ext": "mp4", "protocol": "https", "vcodec": "h264", "acodec": "aac", "height": 720, "tbr": 1500, "filesize": 1048576 }
          ]
        }
        """;

        var r = SiteExtractor.Select(json);

        Assert.Equal(ExtractionKind.Progressive, r.Kind);
        Assert.Equal("https://cdn/video.mp4", r.PrimaryUrl);
        Assert.Equal("Cool clip.mp4", r.FileName);
        Assert.Equal(1048576, r.PrimarySize);
        Assert.NotNull(r.Headers);
        Assert.Equal("yt-dlp/test", r.Headers!["User-Agent"]);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Select_treats_codecless_http_mp4_with_dimensions_as_progressive()
    {
        // x.com progressive MP4s carry NO vcodec/acodec fields but are muxed video+audio in practice.
        // They must win over a video-only HLS variant (which produced a silent — and broken — download).
        const string json = """
        {
          "title": "Tweet video",
          "formats": [
            { "format_id": "hls-1436", "url": "https://video.twimg.com/pl/720.m3u8", "protocol": "m3u8_native", "vcodec": "h264", "acodec": "none", "height": 720, "tbr": 1436 },
            { "format_id": "http-950", "url": "https://video.twimg.com/vid/480/a.mp4", "ext": "mp4", "protocol": "https", "height": 480, "tbr": 950 },
            { "format_id": "http-2176", "url": "https://video.twimg.com/vid/720/b.mp4", "ext": "mp4", "protocol": "https", "height": 720, "tbr": 2176 }
          ]
        }
        """;

        var r = SiteExtractor.Select(json);

        Assert.Equal(ExtractionKind.Progressive, r.Kind);
        Assert.Equal("https://video.twimg.com/vid/720/b.mp4", r.PrimaryUrl);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Select_picks_hls_when_no_progressive()
    {
        const string json = """
        {
          "title": "HLS clip",
          "formats": [
            { "format_id": "hls-720", "url": "https://cdn/720/index.m3u8", "ext": "mp4", "protocol": "m3u8_native", "vcodec": "h264", "acodec": "aac", "height": 720 },
            { "format_id": "hls-360", "url": "https://cdn/360/index.m3u8", "ext": "mp4", "protocol": "m3u8_native", "vcodec": "h264", "acodec": "aac", "height": 360 }
          ]
        }
        """;

        var r = SiteExtractor.Select(json);

        Assert.Equal(ExtractionKind.Hls, r.Kind);
        Assert.Equal("https://cdn/720/index.m3u8", r.PrimaryUrl); // best (highest) variant
        Assert.Equal("HLS clip.mp4", r.FileName);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Select_picks_video_plus_audio_when_only_split_streams()
    {
        const string json = """
        {
          "title": "Split clip",
          "requested_formats": [
            { "format_id": "v", "url": "https://cdn/v.mp4", "ext": "mp4", "protocol": "https", "vcodec": "vp9", "acodec": "none", "height": 1080, "filesize": 5000 },
            { "format_id": "a", "url": "https://cdn/a.m4a", "ext": "m4a", "protocol": "https", "vcodec": "none", "acodec": "opus", "filesize": 900 }
          ],
          "formats": [
            { "format_id": "v", "url": "https://cdn/v.mp4", "ext": "mp4", "protocol": "https", "vcodec": "vp9", "acodec": "none", "height": 1080, "filesize": 5000 },
            { "format_id": "a", "url": "https://cdn/a.m4a", "ext": "m4a", "protocol": "https", "vcodec": "none", "acodec": "opus", "filesize": 900 }
          ]
        }
        """;

        var r = SiteExtractor.Select(json);

        Assert.Equal(ExtractionKind.VideoAudio, r.Kind);
        Assert.Equal("https://cdn/v.mp4", r.VideoUrl);
        Assert.Equal("https://cdn/a.m4a", r.AudioUrl);
        Assert.Equal(5000, r.VideoSize);
        Assert.Equal(900, r.AudioSize);
        Assert.Equal("Split clip.mp4", r.FileName);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Select_does_not_cap_quality_at_a_low_progressive_when_taller_split_streams_exist()
    {
        // YouTube shape: the ONLY progressive combined format is 360p (format 18), while video-only goes
        // to 1080p and premuxed HLS variants also reach 1080p. Preferring "simple" here downloaded every
        // YouTube video at 360p. The taller HLS stream must win.
        const string json = """
        {
          "title": "YT clip",
          "formats": [
            { "format_id": "18", "url": "https://cdn/prog360.mp4", "ext": "mp4", "protocol": "https", "vcodec": "h264", "acodec": "aac", "height": 360, "tbr": 500 },
            { "format_id": "96", "url": "https://cdn/hls1080.m3u8", "ext": "mp4", "protocol": "m3u8_native", "vcodec": "h264", "acodec": "aac", "height": 1080, "tbr": 2500 },
            { "format_id": "v1080", "url": "https://cdn/v1080.mp4", "ext": "mp4", "protocol": "https", "vcodec": "vp9", "acodec": "none", "height": 1080, "tbr": 2000 },
            { "format_id": "a", "url": "https://cdn/a.m4a", "ext": "m4a", "protocol": "https", "vcodec": "none", "acodec": "opus", "tbr": 128 }
          ]
        }
        """;

        var r = SiteExtractor.Select(json);

        Assert.Equal(ExtractionKind.Hls, r.Kind);
        Assert.Equal("https://cdn/hls1080.m3u8", r.PrimaryUrl);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Select_muxes_split_streams_when_they_beat_the_progressive_and_no_hls()
    {
        const string json = """
        {
          "title": "YT clip",
          "formats": [
            { "format_id": "18", "url": "https://cdn/prog360.mp4", "ext": "mp4", "protocol": "https", "vcodec": "h264", "acodec": "aac", "height": 360, "tbr": 500 },
            { "format_id": "v1080", "url": "https://cdn/v1080.mp4", "ext": "mp4", "protocol": "https", "vcodec": "vp9", "acodec": "none", "height": 1080, "tbr": 2000 },
            { "format_id": "a", "url": "https://cdn/a.m4a", "ext": "m4a", "protocol": "https", "vcodec": "none", "acodec": "opus", "tbr": 128 }
          ]
        }
        """;

        var r = SiteExtractor.Select(json);

        Assert.Equal(ExtractionKind.VideoAudio, r.Kind);
        Assert.Equal("https://cdn/v1080.mp4", r.VideoUrl);
        Assert.Equal("https://cdn/a.m4a", r.AudioUrl);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Select_keeps_progressive_when_split_streams_are_not_taller()
    {
        const string json = """
        {
          "title": "Clip",
          "formats": [
            { "format_id": "p", "url": "https://cdn/prog720.mp4", "ext": "mp4", "protocol": "https", "vcodec": "h264", "acodec": "aac", "height": 720, "tbr": 1500 },
            { "format_id": "v", "url": "https://cdn/v720.mp4", "ext": "mp4", "protocol": "https", "vcodec": "vp9", "acodec": "none", "height": 720, "tbr": 1400 },
            { "format_id": "a", "url": "https://cdn/a.m4a", "ext": "m4a", "protocol": "https", "vcodec": "none", "acodec": "opus", "tbr": 128 }
          ]
        }
        """;

        var r = SiteExtractor.Select(json);

        Assert.Equal(ExtractionKind.Progressive, r.Kind);
        Assert.Equal("https://cdn/prog720.mp4", r.PrimaryUrl);
    }

    // YouTube-shaped canned JSON reused by the variant tests: 360p progressive, 720p+1080p split
    // video-only, an HLS 1080p premux and one audio-only stream.
    private const string VariantJson = """
    {
      "title": "YT clip",
      "formats": [
        { "format_id": "18", "url": "https://cdn/prog360.mp4", "ext": "mp4", "protocol": "https", "vcodec": "h264", "acodec": "aac", "height": 360, "tbr": 500, "filesize": 1000 },
        { "format_id": "96", "url": "https://cdn/hls1080.m3u8", "ext": "mp4", "protocol": "m3u8_native", "vcodec": "h264", "acodec": "aac", "height": 1080, "tbr": 2500 },
        { "format_id": "v720", "url": "https://cdn/v720.mp4", "ext": "mp4", "protocol": "https", "vcodec": "vp9", "acodec": "none", "height": 720, "tbr": 1200, "filesize": 3000 },
        { "format_id": "v1080", "url": "https://cdn/v1080.mp4", "ext": "mp4", "protocol": "https", "vcodec": "vp9", "acodec": "none", "height": 1080, "tbr": 2000, "filesize": 5000 },
        { "format_id": "a", "url": "https://cdn/a.m4a", "ext": "m4a", "protocol": "https", "vcodec": "none", "acodec": "opus", "tbr": 128, "filesize": 900 }
      ]
    }
    """;

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void ListVariants_returns_heights_desc_plus_audio_with_default_on_best()
    {
        var variants = SiteExtractor.ListVariants(VariantJson);

        Assert.Equal(new[] { "1080", "720", "360", "audio" }, variants.Select(v => v.Id));
        Assert.True(variants[0].IsDefault);
        Assert.All(variants.Skip(1), v => Assert.False(v.IsDefault));
        Assert.Equal(5900, variants.Single(v => v.Id == "1080").ExpectedSize); // v1080 + audio
        Assert.Equal(1000, variants.Single(v => v.Id == "360").ExpectedSize);  // combined progressive
        Assert.Contains("Audio only", variants.Single(v => v.Id == "audio").Label);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void ListVariants_offers_no_choice_for_a_single_quality_without_audio()
    {
        const string json = """
        {
          "title": "One",
          "formats": [
            { "format_id": "p", "url": "https://cdn/only.mp4", "ext": "mp4", "protocol": "https", "vcodec": "h264", "acodec": "aac", "height": 480 }
          ]
        }
        """;
        Assert.Empty(SiteExtractor.ListVariants(json));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Select_pins_to_the_requested_height()
    {
        var r = SiteExtractor.Select(VariantJson, "720");

        Assert.Equal(ExtractionKind.VideoAudio, r.Kind); // only a split stream exists at 720
        Assert.Equal("https://cdn/v720.mp4", r.VideoUrl);
        Assert.Equal("https://cdn/a.m4a", r.AudioUrl);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Select_audio_variant_returns_the_best_audio_stream()
    {
        var r = SiteExtractor.Select(VariantJson, "audio");

        Assert.Equal(ExtractionKind.Progressive, r.Kind);
        Assert.Equal("https://cdn/a.m4a", r.PrimaryUrl);
        Assert.EndsWith(".m4a", r.FileName);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Select_null_variant_keeps_the_automatic_pick()
    {
        var r = SiteExtractor.Select(VariantJson, null);

        Assert.Equal(ExtractionKind.Hls, r.Kind); // 1080p HLS beats the 360p progressive
        Assert.Equal("https://cdn/hls1080.m3u8", r.PrimaryUrl);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Select_sanitizes_title_into_filename()
    {
        const string json = """
        { "title": "a/b:c*we?ird", "formats": [ { "format_id": "p", "url": "https://cdn/v.mp4", "ext": "mp4", "protocol": "https", "vcodec": "h264", "acodec": "aac" } ] }
        """;

        var r = SiteExtractor.Select(json);

        Assert.DoesNotContain('/', r.FileName);
        Assert.DoesNotContain(':', r.FileName);
        Assert.DoesNotContain('*', r.FileName);
        Assert.DoesNotContain('?', r.FileName);
        Assert.EndsWith(".mp4", r.FileName);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Select_falls_back_to_id_when_no_title()
    {
        const string json = """
        { "id": "vid42", "formats": [ { "format_id": "p", "url": "https://cdn/v.mp4", "ext": "mp4", "protocol": "https", "vcodec": "h264", "acodec": "aac" } ] }
        """;

        var r = SiteExtractor.Select(json);
        Assert.Equal("vid42.mp4", r.FileName);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Select_throws_clear_error_on_no_media()
    {
        const string json = """ { "title": "empty", "formats": [] } """;
        var ex = Assert.Throws<InvalidOperationException>(() => SiteExtractor.Select(json));
        Assert.Contains("No downloadable video", ex.Message);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Select_throws_clear_error_on_bad_json()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SiteExtractor.Select("not-json{"));
        Assert.Contains("video information", ex.Message);
    }
}

public class YtDlpCookieRetryTests
{
    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("ERROR: [youtube] abc: Sign in to confirm you’re not a bot. Use --cookies-from-browser", true)]
    [InlineData("ERROR: This video is age-restricted; log in to watch", true)]
    [InlineData("ERROR: [twitter] 643211948184596480: No video could be found in this tweet", true)]
    [InlineData("ERROR: Unsupported URL: https://example.com", false)]
    [InlineData("", false)]
    public void NeedsCookies_detects_signin_errors(string stderr, bool expected) =>
        Assert.Equal(expected, YtDlpBinary.NeedsCookies(stderr));
}
