namespace Downloader.Desktop.Plugins.Hls;

/// <summary>
/// Parses M3U8 playlist text. Kept behind an interface so the resolver can be unit-tested with a fake
/// parser, and the parser itself tested in isolation against committed fixtures.
/// </summary>
public interface IM3u8Parser
{
    /// <summary>True when the playlist is a master playlist (contains <c>#EXT-X-STREAM-INF</c> variants).</summary>
    bool IsMaster(string content);

    /// <summary>Parse a master playlist; variant URIs are resolved against <paramref name="baseUri"/>.</summary>
    HlsMasterPlaylist ParseMaster(string content, Uri baseUri);

    /// <summary>Parse a media playlist; segment/key/init URIs are resolved against <paramref name="baseUri"/>.</summary>
    HlsMediaPlaylist ParseMedia(string content, Uri baseUri);
}
