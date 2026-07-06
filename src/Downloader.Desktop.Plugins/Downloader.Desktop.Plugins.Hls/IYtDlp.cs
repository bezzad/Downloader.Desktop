namespace Downloader.Desktop.Plugins.Hls;

/// <summary>
/// Abstracts the one yt-dlp operation the site extractor needs: run <c>yt-dlp -J</c> (dump the media
/// metadata as JSON, without downloading) for a page URL and return the raw JSON. Behind an interface so
/// tests stub it with canned fixtures — extraction logic is verified with no network and no real binary,
/// mirroring how <see cref="IFfmpeg"/> is stubbed. The real implementation
/// (<see cref="YtDlpBinary"/>) downloads the correct per-OS yt-dlp build on first use.
/// </summary>
public interface IYtDlp
{
    /// <summary>Run <c>yt-dlp -J &lt;url&gt;</c> and return its JSON stdout. Throws a clear, logged error if
    /// yt-dlp cannot be provisioned or the extraction process fails.</summary>
    Task<string> ExtractJsonAsync(string url, CancellationToken cancellationToken);
}
