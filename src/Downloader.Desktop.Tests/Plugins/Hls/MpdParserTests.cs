using Downloader.Desktop.Plugins.Hls.Dash;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins.Hls;

/// <summary>
/// The MPD parser against committed fixtures — one per addressing mode the spec allows, plus the two
/// manifests we deliberately refuse. These assert the expanded, absolute segment URLs: getting those wrong
/// is the failure mode that produces a "downloaded" file which won't play.
/// </summary>
public class MpdParserTests
{
    private static readonly Uri ManifestUri = new("https://host.example/path/manifest.mpd");

    private static DashManifest Parse(string fixture) =>
        new MpdParser().Parse(TestFixtures.Read(fixture), ManifestUri);

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void SegmentTemplate_with_a_timeline_expands_each_S_entry()
    {
        var manifest = Parse("dash-timeline.mpd");

        Assert.Equal(100, manifest.DurationSeconds);
        Assert.Equal(2, manifest.Video.Count);
        Assert.Single(manifest.Audio);

        var best = manifest.BestVideo()!;
        Assert.Equal("v1080", best.Id);
        Assert.Equal(4_800_000, best.Bandwidth);
        Assert.Equal(1080, best.Height);
        Assert.Equal("https://cdn.example.com/vod/v1080/init.mp4", best.InitSegmentUri);

        // <S t="0" d="4000" r="2"/> is three segments at 0/4000/8000, then <S d="2000"/> continues at 12000.
        Assert.Equal(new[]
        {
            "https://cdn.example.com/vod/v1080/seg-0.m4s",
            "https://cdn.example.com/vod/v1080/seg-4000.m4s",
            "https://cdn.example.com/vod/v1080/seg-8000.m4s",
            "https://cdn.example.com/vod/v1080/seg-12000.m4s",
        }, best.SegmentUris);

        var audio = manifest.BestAudio()!;
        Assert.Equal("a128", audio.Id);
        Assert.Equal("en", audio.Language);
        Assert.Equal("https://cdn.example.com/vod/a128/seg-0.m4s", audio.SegmentUris[0]);
        Assert.False(audio.IsSingleFile);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void SegmentTemplate_without_a_timeline_derives_the_count_from_the_duration()
    {
        var manifest = Parse("dash-number.mpd");

        // 20s total, 450000/90000 = 5s per segment → 4 segments numbered from 1, zero-padded to 4 digits.
        var video = manifest.BestVideo()!;
        Assert.Equal(new[]
        {
            "https://host.example/path/video/1500000/chunk-0001.m4s",
            "https://host.example/path/video/1500000/chunk-0002.m4s",
            "https://host.example/path/video/1500000/chunk-0003.m4s",
            "https://host.example/path/video/1500000/chunk-0004.m4s",
        }, video.SegmentUris);

        // $Bandwidth$ is substituted in the initialization template too.
        Assert.Equal("https://host.example/path/video/1500000/init.mp4", video.InitSegmentUri);
        Assert.Equal(4, manifest.BestAudio()!.SegmentUris.Count);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void SegmentList_uses_the_declared_segment_urls_and_inherits_nested_base_urls()
    {
        var manifest = Parse("dash-segmentlist.mpd");

        var video = manifest.BestVideo()!;
        // MPD BaseURL + Period BaseURL + Representation BaseURL compose in that order.
        Assert.Equal("https://cdn.example.com/base/period1/v1/init.mp4", video.InitSegmentUri);
        Assert.Equal(new[]
        {
            "https://cdn.example.com/base/period1/v1/s1.m4s",
            "https://cdn.example.com/base/period1/v1/s2.m4s",
            "https://cdn.example.com/base/period1/v1/s3.m4s",
        }, video.SegmentUris);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void SegmentBase_and_a_bare_BaseURL_are_single_complete_files()
    {
        var manifest = Parse("dash-segmentbase.mpd");

        var video = manifest.BestVideo()!;
        Assert.True(video.IsSingleFile);
        Assert.Null(video.InitSegmentUri);
        Assert.Equal(new[] { "https://cdn.example.com/od/movie-video.mp4" }, video.SegmentUris);

        var audio = manifest.BestAudio()!;
        Assert.True(audio.IsSingleFile);
        Assert.Equal(new[] { "https://cdn.example.com/od/movie-audio.mp4" }, audio.SegmentUris);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_manifest_without_a_namespace_still_parses()
    {
        var manifest = Parse("dash-nonamespace.mpd");

        var video = manifest.BestVideo()!;
        Assert.Equal(240, video.Height);
        // startNumber="0", 8s / 4s = 2 segments.
        Assert.Equal(new[]
        {
            "https://host.example/path/s0.m4s",
            "https://host.example/path/s1.m4s",
        }, video.SegmentUris);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_live_manifest_is_refused_with_a_reason()
    {
        var ex = Assert.Throws<DashException>(() => Parse("dash-live.mpd"));
        Assert.Contains("live", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_drm_protected_manifest_is_refused_with_a_reason()
    {
        var ex = Assert.Throws<DashException>(() => Parse("dash-drm.mpd"));
        Assert.Contains("DRM", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Content_that_is_not_a_manifest_is_refused()
    {
        Assert.Throws<DashException>(() => new MpdParser().Parse("not xml at all", ManifestUri));
        Assert.Throws<DashException>(() => new MpdParser().Parse("<html><body/></html>", ManifestUri));
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("x-$Number$.m4s", "x-7.m4s")]
    [InlineData("x-$Number%05d$.m4s", "x-00007.m4s")]
    [InlineData("x-$Time$.m4s", "x-4000.m4s")]
    [InlineData("$RepresentationID$/$Bandwidth$/s.m4s", "v1/900/s.m4s")]
    [InlineData("cost$$-$Number$", "cost$-7")]
    [InlineData("keep-$Unknown$", "keep-$Unknown$")]
    public void Template_placeholders_are_substituted(string template, string expected) =>
        Assert.Equal(expected, MpdParser.Substitute(template, "v1", 900, number: 7, time: 4000));
}
