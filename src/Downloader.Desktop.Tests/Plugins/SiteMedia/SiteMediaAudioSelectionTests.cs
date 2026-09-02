using Downloader.Desktop.Plugins;
using Downloader.Desktop.Plugins.SiteMedia;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins.SiteMedia;

/// <summary>
/// Which audio stream gets muxed into the MP4. Opus is what the extraction tool reports as "best audio"
/// for most video pages, and it CAN be written into MP4 — but most desktop players will not decode it
/// there, so the finished video plays without a sound, exactly as if no audio had been downloaded at all.
/// An MP4-native track (AAC) is therefore preferred over a higher-bitrate foreign one.
/// </summary>
public class SiteMediaAudioSelectionTests
{
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_mp4_native_audio_track_wins_over_a_higher_bitrate_opus_one()
    {
        // Shaped like a real YouTube extraction: opus has the higher bitrate AND is the tool's own pick.
        const string json = """
        { "title": "A talk", "requested_formats": [
            { "format_id": "248", "url": "https://cdn/v1080.webm", "protocol": "https", "vcodec": "vp9", "acodec": "none", "height": 1080 },
            { "format_id": "251", "url": "https://cdn/a.webm", "protocol": "https", "vcodec": "none", "acodec": "opus", "ext": "webm", "tbr": 130 } ],
          "formats": [
            { "format_id": "137", "url": "https://cdn/v1080.mp4", "protocol": "https", "vcodec": "avc1.640028", "acodec": "none", "height": 1080 },
            { "format_id": "251", "url": "https://cdn/a.webm", "protocol": "https", "vcodec": "none", "acodec": "opus", "ext": "webm", "tbr": 130 },
            { "format_id": "140", "url": "https://cdn/a.m4a", "protocol": "https", "vcodec": "none", "acodec": "mp4a.40.2", "ext": "m4a", "tbr": 129 } ] }
        """;
        var resolver = new SiteMediaResolver(new SiteMediaResolverTests.StubYtDlp(json));

        var plan = await resolver.ResolveAsync("https://www.youtube.com/watch?v=abc",
            TestContext.Current.CancellationToken);

        Assert.Equal(2, plan.Parts.Count);
        Assert.Equal(PartKind.Audio, plan.Parts[1].Kind);
        Assert.Equal("https://cdn/a.m4a", plan.Parts[1].Url);
        Assert.Equal(PostProcessKind.Mux, plan.PostProcess.Kind);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Opus_is_still_used_when_it_is_the_only_audio_there_is()
    {
        // Silent-ish playback in some players beats no audio track at all — never refuse the download.
        const string json = """
        { "title": "A talk", "formats": [
            { "format_id": "248", "url": "https://cdn/v1080.webm", "protocol": "https", "vcodec": "vp9", "acodec": "none", "height": 1080 },
            { "format_id": "251", "url": "https://cdn/a.webm", "protocol": "https", "vcodec": "none", "acodec": "opus", "ext": "webm", "tbr": 130 } ] }
        """;
        var resolver = new SiteMediaResolver(new SiteMediaResolverTests.StubYtDlp(json));

        var plan = await resolver.ResolveAsync("https://www.youtube.com/watch?v=abc",
            TestContext.Current.CancellationToken);

        Assert.Equal("https://cdn/a.webm", plan.Parts[1].Url);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("mp4a.40.2", null, true)]
    [InlineData("aac", null, true)]
    [InlineData("ec-3", null, true)]
    [InlineData("opus", null, false)]
    [InlineData("vorbis", null, false)]
    // No codec reported: fall back to the container, since an extracted URL has no file extension.
    [InlineData(null, "m4a", true)]
    [InlineData("none", "m4a", true)]
    [InlineData(null, "webm", false)]
    [InlineData(null, null, false)]
    public void Mp4_native_audio_is_judged_by_codec_then_container(string? acodec, string? ext, bool expected)
    {
        var format = new YtDlpFormat { ACodec = acodec, Ext = ext };

        Assert.Equal(expected, SiteExtractor.IsMp4NativeAudio(format));
    }
}
