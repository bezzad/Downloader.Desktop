using Downloader.Desktop.Plugins.Hls;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins.Hls;

/// <summary>
/// Unit tests for the pure format-selection core: canned <c>yt-dlp -J</c> JSON → the chosen
/// <see cref="ExtractionResult"/>. No network, no real binary (internals are visible to this test project).
/// </summary>
public class SiteExtractorTests
{
    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public void Select_falls_back_to_id_when_no_title()
    {
        const string json = """
        { "id": "vid42", "formats": [ { "format_id": "p", "url": "https://cdn/v.mp4", "ext": "mp4", "protocol": "https", "vcodec": "h264", "acodec": "aac" } ] }
        """;

        var r = SiteExtractor.Select(json);
        Assert.Equal("vid42.mp4", r.FileName);
    }

    [Fact]
    public void Select_throws_clear_error_on_no_media()
    {
        const string json = """ { "title": "empty", "formats": [] } """;
        var ex = Assert.Throws<InvalidOperationException>(() => SiteExtractor.Select(json));
        Assert.Contains("No downloadable video", ex.Message);
    }

    [Fact]
    public void Select_throws_clear_error_on_bad_json()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SiteExtractor.Select("not-json{"));
        Assert.Contains("video information", ex.Message);
    }
}

public class YtDlpCookieRetryTests
{
    [Theory]
    [InlineData("ERROR: [youtube] abc: Sign in to confirm you’re not a bot. Use --cookies-from-browser", true)]
    [InlineData("ERROR: This video is age-restricted; log in to watch", true)]
    [InlineData("ERROR: Unsupported URL: https://example.com", false)]
    [InlineData("", false)]
    public void NeedsCookies_detects_signin_errors(string stderr, bool expected) =>
        Assert.Equal(expected, YtDlpBinary.NeedsCookies(stderr));
}
