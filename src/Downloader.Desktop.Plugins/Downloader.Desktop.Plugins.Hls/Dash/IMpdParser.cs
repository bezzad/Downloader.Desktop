namespace Downloader.Desktop.Plugins.Hls.Dash;

/// <summary>
/// Parses an MPEG-DASH manifest into fully expanded representations. Behind an interface so the resolver is
/// unit-testable with a fake, and the parser itself is tested in isolation against committed fixtures.
/// </summary>
public interface IMpdParser
{
    /// <summary>
    /// Parse a static MPD. Relative URLs are resolved against the manifest's own (post-redirect) URI and the
    /// <c>BaseURL</c> chain inside it.
    /// </summary>
    /// <exception cref="DashException">The manifest is live, DRM-protected, or unusable.</exception>
    DashManifest Parse(string content, Uri baseUri);
}
