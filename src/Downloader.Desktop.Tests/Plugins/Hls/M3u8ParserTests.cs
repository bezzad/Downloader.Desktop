using Downloader.Desktop.Plugins.Hls;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins.Hls;

public class M3u8ParserTests
{
    private readonly M3u8Parser _parser = new();
    private static readonly Uri MasterBase = new("https://cdn.example.com/video/master.m3u8");
    private static readonly Uri MediaBase = new("https://cdn.example.com/video/low/index.m3u8");

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Detects_master_playlist()
    {
        Assert.True(_parser.IsMaster(TestFixtures.Read("master.m3u8")));
        Assert.False(_parser.IsMaster(TestFixtures.Read("media.m3u8")));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Master_selects_highest_bandwidth_variant()
    {
        var master = _parser.ParseMaster(TestFixtures.Read("master.m3u8"), MasterBase);

        Assert.Equal(3, master.Variants.Count);
        var best = master.Best();
        Assert.Equal(4_800_000, best.Bandwidth);
        Assert.Equal("https://cdn.example.com/video/high/index.m3u8", best.Uri);
        Assert.Equal("1920x1080", best.Resolution);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Media_returns_ordered_segments_with_resolved_uris()
    {
        var media = _parser.ParseMedia(TestFixtures.Read("media.m3u8"), MediaBase);

        Assert.Equal(3, media.Segments.Count);
        Assert.Equal("https://cdn.example.com/video/low/seg0.ts", media.Segments[0].Uri);
        Assert.Equal("https://cdn.example.com/video/low/seg1.ts", media.Segments[1].Uri);
        // Absolute URI in the playlist is preserved as-is.
        Assert.Equal("https://cdn.example.com/abs/seg2.ts", media.Segments[2].Uri);
        Assert.Equal(0, media.Segments[0].MediaSequence);
        Assert.Equal(2, media.Segments[2].MediaSequence);
        Assert.False(media.IsEncrypted);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Media_resolves_root_relative_segment_uris_against_playlist_host()
    {
        // x.com playlists reference segments by root-relative path ("/amplify_video/..."). On Unix,
        // Uri.TryCreate(path, Absolute) parses such a path as file:/// — the old Resolve turned every
        // segment into a file:// URL and the engine failed with "The 'file' scheme is not supported."
        const string playlist = "#EXTM3U\n#EXT-X-VERSION:6\n#EXTINF:3.0,\n/amplify_video/1/vid/avc1/0/3000/1280x720/a.m4s\n#EXT-X-ENDLIST\n";
        var media = _parser.ParseMedia(playlist, new Uri("https://video.twimg.com/amplify_video/1/pl/avc1/1280x720/pl.m3u8"));

        Assert.Equal("https://video.twimg.com/amplify_video/1/vid/avc1/0/3000/1280x720/a.m4s", media.Segments[0].Uri);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Media_parses_aes128_key_uri_and_iv()
    {
        var media = _parser.ParseMedia(TestFixtures.Read("media-aes.m3u8"), MediaBase);

        Assert.True(media.IsEncrypted);
        var key = media.Segments[0].Key!;
        Assert.Equal("AES-128", key.Method);
        Assert.Equal("https://cdn.example.com/video/low/key.bin", key.Uri);
        Assert.NotNull(key.Iv);
        Assert.Equal(16, key.Iv!.Length);
        Assert.All(key.Iv, b => Assert.Equal(0, b));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Media_parses_init_segment_map()
    {
        var media = _parser.ParseMedia(TestFixtures.Read("media-map.m3u8"), MediaBase);

        Assert.Equal("https://cdn.example.com/video/low/init.mp4", media.InitSegmentUri);
        Assert.Equal(2, media.Segments.Count);
        Assert.Equal("https://cdn.example.com/video/low/seg0.m4s", media.Segments[0].Uri);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Implicit_iv_defaults_to_media_sequence()
    {
        const string playlist =
            "#EXTM3U\n" +
            "#EXT-X-MEDIA-SEQUENCE:5\n" +
            "#EXT-X-KEY:METHOD=AES-128,URI=\"k.bin\"\n" +
            "#EXTINF:1.0,\nseg5.ts\n" +
            "#EXTINF:1.0,\nseg6.ts\n";

        var media = _parser.ParseMedia(playlist, MediaBase);

        // sequence 5 -> IV with 0x05 in the last byte
        var iv5 = media.Segments[0].Key!.Iv!;
        Assert.Equal(5, iv5[15]);
        var iv6 = media.Segments[1].Key!.Iv!;
        Assert.Equal(6, iv6[15]);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("")]
    [InlineData("   \n  \n")]
    public void Empty_playlist_throws(string content)
    {
        Assert.Throws<FormatException>(() => _parser.ParseMedia(content, MediaBase));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Garbled_playlist_throws_clear_error()
    {
        var ex = Assert.Throws<FormatException>(
            () => _parser.ParseMedia(TestFixtures.Read("garbled.txt"), MediaBase));
        Assert.Contains("M3U8", ex.Message);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Media_with_no_segments_throws()
    {
        const string playlist = "#EXTM3U\n#EXT-X-TARGETDURATION:10\n#EXT-X-ENDLIST\n";
        Assert.Throws<FormatException>(() => _parser.ParseMedia(playlist, MediaBase));
    }
}
