namespace Downloader.Desktop.Plugins.Hls.Dash;

/// <summary>
/// A DASH manifest that cannot be turned into a download, with a message meant for the user (the failed row
/// shows it). Thrown for the two honest refusals — a live stream and a DRM-protected one — and for a
/// manifest we can't make sense of, so the reason is never swallowed into a generic failure.
/// </summary>
public sealed class DashException : Exception
{
    public DashException(string message) : base(message) { }
}
