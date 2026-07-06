namespace Downloader.Desktop.Plugins.Hls;

/// <summary>
/// Optional, injected probe that lets <see cref="HlsResolver.CanResolve"/> recognize an HLS link whose URL
/// does not end in <c>.m3u8</c> by inspecting its content type. Kept out of the default path so
/// <c>CanResolve</c> stays a cheap, network-free check unless a probe is supplied.
/// </summary>
public interface IContentTypeProbe
{
    /// <summary>True if a (cheap, short-timeout) check reports the URL serves an HLS playlist content type.</summary>
    bool LooksLikeHls(string url);
}
