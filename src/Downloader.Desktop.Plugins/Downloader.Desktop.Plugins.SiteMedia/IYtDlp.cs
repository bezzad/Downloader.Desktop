namespace Downloader.Desktop.Plugins.SiteMedia;

/// <summary>
/// Abstracts the one extraction-tool operation this plugin needs: dump a page's media metadata as JSON
/// without downloading anything. Behind an interface so the whole selection policy is unit-tested against
/// recorded output — no network, no binary, no site. The real implementation
/// (<see cref="YtDlpBinary"/>) fetches and checksum-verifies the tool on first use.
/// </summary>
public interface IYtDlp
{
    /// <summary>Extract <paramref name="url"/> anonymously and return the tool's JSON stdout. Throws a
    /// clear, already-logged error when the tool cannot be provisioned or the extraction fails.</summary>
    Task<string> ExtractJsonAsync(string url, CancellationToken cancellationToken);

    /// <summary>Same, but using a Netscape cookie file captured from the user's live browser session by
    /// our own extension. Default-implemented to ignore the file so simple stubs keep working. There is
    /// deliberately no "read the cookies out of the browser" path — see the plugin's csproj.</summary>
    Task<string> ExtractJsonAsync(string url, string? cookieFilePath, CancellationToken cancellationToken)
        => ExtractJsonAsync(url, cancellationToken);
}
