using System.IO.Compression;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Downloader.Desktop.Plugins;

namespace Downloader.Desktop.Plugins.Website;

internal sealed class WebsiteTransferProvider : ITransferProvider
{
    private readonly ILogger? _logger;

    public WebsiteTransferProvider(ILogger? logger = null) => _logger = logger;

    public bool CanHandle(string url) => WebsiteResolver.IsSchemeUrl(url);

    public ITransfer Create(string url, string targetFolder) => new WebsiteTransfer(url, targetFolder, _logger);
}

/// <summary>
/// Owns one offline-copy download end to end: crawl the site into a temp working directory, zip it into
/// the target folder, clean up. Progress is fraction-of-documents based (the total grows as links are
/// discovered, so the reported percentage is clamped monotonic); speed is a sliding byte window.
/// </summary>
internal sealed class WebsiteTransfer : ITransfer
{
    private static readonly HttpClient Http = CreateClient();

    private readonly string _url;
    private readonly string _targetFolder;
    private readonly ILogger? _logger;
    private readonly SiteCrawler _crawler;

    private double _lastPercentage;
    private readonly Queue<(DateTime At, long Bytes)> _speedWindow = new();

    public event EventHandler<TransferProgress>? ProgressChanged;

    public WebsiteTransfer(string url, string targetFolder, ILogger? logger = null)
    {
        _url = WebsiteResolver.IsSchemeUrl(url) ? url[WebsiteResolver.Scheme.Length..] : url;
        _targetFolder = targetFolder;
        _logger = logger;
        _crawler = new SiteCrawler(Http, logger: logger) { Progress = OnCrawlProgress };
    }

    public async Task<string> StartAsync(CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(_url, UriKind.Absolute, out var root) ||
            (root.Scheme != Uri.UriSchemeHttp && root.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException($"Not a web page link: {_url}");

        var workDir = Path.Combine(Path.GetTempPath(), "downloader-website-" + Guid.NewGuid().ToString("N"));
        try
        {
            var captured = await _crawler.CrawlAsync(root, workDir, cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("Captured {Count} documents from {Host}", captured, root.Host);

            Directory.CreateDirectory(_targetFolder);
            var zipPath = UniquePath(Path.Combine(_targetFolder, SuggestedZipName(root)));
            ZipFile.CreateFromDirectory(workDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            return zipPath;
        }
        finally
        {
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); }
            catch (Exception ex) { _logger?.LogWarning("Couldn't remove temp crawl folder: {Error}", ex.Message); }
        }
    }

    public void Pause() => _crawler.Pause();

    public void Resume() => _crawler.Resume();

    internal static string SuggestedZipName(Uri root)
    {
        var host = root.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? root.Host[4..] : root.Host;
        return host + ".zip";
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
            return path;
        var dir = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}).zip");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    private void OnCrawlProgress(long bytes, int done, int discovered)
    {
        // The denominator grows while links are discovered — clamp so the bar never moves backwards.
        var pct = discovered > 0 ? done * 100.0 / discovered : 0;
        _lastPercentage = Math.Max(_lastPercentage, Math.Min(pct, 99.9));

        var now = DateTime.UtcNow;
        _speedWindow.Enqueue((now, bytes));
        while (_speedWindow.Count > 1 && (now - _speedWindow.Peek().At) > TimeSpan.FromSeconds(3))
            _speedWindow.Dequeue();
        var (oldAt, oldBytes) = _speedWindow.Peek();
        var seconds = (now - oldAt).TotalSeconds;
        var speed = seconds > 0.2 ? (bytes - oldBytes) / seconds : 0;

        ProgressChanged?.Invoke(this, new TransferProgress
        {
            Percentage = _lastPercentage,
            BytesReceived = bytes,
            TotalBytes = bytes, // real total is unknowable up front — show what's been captured
            BytesPerSecond = Math.Max(0, speed)
        });
    }

    private static HttpClient CreateClient()
    {
        // Per-request timeouts are enforced by the crawler; the client itself must not cut off a long crawl.
        var client = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; Downloader)");
        return client;
    }
}
