using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Downloader.Desktop.Plugins.Website;

internal sealed class CrawlOptions
{
    /// <summary>How many link hops from the start page same-host pages are followed.</summary>
    public int MaxDepth { get; init; } = 3;
    public int MaxPages { get; init; } = 200;
    public int MaxAssets { get; init; } = 2000;
    /// <summary>Per-request guard so one huge file can't eat the disk (50 MB).</summary>
    public long MaxDocumentBytes { get; init; } = 50 * 1024 * 1024;
    /// <summary>Whole-request timeout per document.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// BFS crawl of a site into a local directory mirror: same-host HTML pages recurse (depth/page caps),
/// page requisites (stylesheets/scripts/images/fonts/media — from any host, including assets referenced
/// inside CSS) download once (asset cap), and every captured reference is rewritten to a relative local
/// path. Sequential and pause-aware: the gate is checked before each request, so Pause() suspends the
/// crawl between documents and cancellation is observed while paused.
/// </summary>
internal sealed class SiteCrawler
{
    private enum DocType { Page, Css, Asset }

    private readonly HttpClient _http;
    private readonly CrawlOptions _options;
    private readonly ILogger? _logger;

    // URL → decided-at-enqueue local path (deterministic, so early documents can point at late ones).
    private readonly Dictionary<string, string> _localPathByUrl = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedLocalPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<(Uri Url, int Depth, DocType Type)> _queue = new();

    private string? _rootHost;
    private int _pages, _assets, _done;
    private long _bytes;
    private volatile TaskCompletionSource<bool>? _pauseTcs;

    /// <summary>(totalBytes, docsDone, docsDiscovered) after every document.</summary>
    public Action<long, int, int>? Progress { get; set; }

    public SiteCrawler(HttpClient http, CrawlOptions? options = null, ILogger? logger = null)
    {
        _http = http;
        _options = options ?? new CrawlOptions();
        _logger = logger;
    }

    public void Pause() => Interlocked.CompareExchange(ref _pauseTcs,
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously), null);

    public void Resume() => Interlocked.Exchange(ref _pauseTcs, null)?.TrySetResult(true);

    /// <summary>Crawls <paramref name="root"/> into <paramref name="workDir"/>. Returns the number of
    /// captured documents. Throws when even the start page cannot be fetched.</summary>
    public async Task<int> CrawlAsync(Uri root, string workDir, CancellationToken ct)
    {
        Directory.CreateDirectory(workDir);
        EnqueuePage(root, 0);

        var isFirst = true;
        while (_queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var gate = _pauseTcs;
            if (gate != null)
                await gate.Task.WaitAsync(ct).ConfigureAwait(false);

            var (url, depth, type) = _queue.Dequeue();
            try
            {
                await FetchOneAsync(url, depth, type, workDir, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // user cancel — the transfer maps this to Stopped
            }
            catch (Exception ex)
            {
                // One broken/slow reference must not kill the whole capture — except the very start
                // page, where "nothing could be fetched" is a real failure the user must see (a
                // per-request timeout surfaces here as OperationCanceledException too).
                if (isFirst)
                    throw new InvalidOperationException(
                        $"Could not fetch the page ({(ex is OperationCanceledException ? "timed out" : ex.Message)})", ex);
                _logger?.LogWarning("Skipped {Url}: {Error}", url, ex.Message);
            }

            isFirst = false;
            _done++;
            Progress?.Invoke(_bytes, _done, _done + _queue.Count);
        }
        return _done;
    }

    private async Task FetchOneAsync(Uri url, int depth, DocType type, string workDir, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_options.RequestTimeout);

        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength is { } len && len > _options.MaxDocumentBytes)
        {
            _logger?.LogWarning("Skipped {Url}: larger than the per-document cap", url);
            return;
        }

        // The first fetch pins the canonical host (the pasted URL may redirect http→https / to www.).
        var finalUrl = resp.RequestMessage?.RequestUri ?? url;
        _rootHost ??= finalUrl.Host;

        var localPath = _localPathByUrl[Key(url)];
        var fullPath = Path.Combine(workDir, localPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var mediaType = resp.Content.Headers.ContentType?.MediaType;
        if (type == DocType.Page && WebsiteResolver.IsHtml(mediaType))
        {
            var html = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            _bytes += html.Length;
            var rewritten = RewriteDocument(html, LinkExtractor.ExtractHtmlRefs(html), finalUrl, depth, localPath);
            await File.WriteAllTextAsync(fullPath, rewritten, cts.Token).ConfigureAwait(false);
        }
        else if (type == DocType.Css || IsCss(mediaType))
        {
            var css = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            _bytes += css.Length;
            var rewritten = RewriteDocument(css, LinkExtractor.ExtractCssRefs(css), finalUrl, depth, localPath);
            await File.WriteAllTextAsync(fullPath, rewritten, cts.Token).ConfigureAwait(false);
        }
        else
        {
            // A "page" that turned out to be a file (a PDF behind an <a>) is saved as-is at its mapped path.
            await using var target = File.Create(fullPath);
            await resp.Content.CopyToAsync(target, cts.Token).ConfigureAwait(false);
            _bytes += target.Length;
        }
    }

    /// <summary>Classifies + enqueues each reference and splices in the rewritten value: captured targets
    /// become relative local paths; everything else becomes its absolute URL so it still works online.</summary>
    private string RewriteDocument(string text, List<UrlRef> refs, Uri documentUrl, int depth, string documentLocalPath)
    {
        var replacements = new List<(UrlRef, string)>(refs.Count);
        foreach (var r in refs)
        {
            if (!LinkExtractor.TryNormalize(documentUrl, r.Value, out var abs))
                continue;

            string? targetLocal = r.Kind switch
            {
                RefKind.PageLink when SameSite(abs) && depth < _options.MaxDepth => EnqueuePage(abs, depth + 1),
                RefKind.PageLink => null,
                RefKind.Stylesheet => EnqueueAsset(abs, depth, DocType.Css),
                _ => EnqueueAsset(abs, depth, DocType.Asset)
            };

            replacements.Add((r, targetLocal != null
                ? LocalPathMapper.RelativePath(documentLocalPath, targetLocal)
                : abs.ToString()));
        }
        return LinkExtractor.Rewrite(text, replacements);
    }

    /// <summary>Local path for a same-site page, enqueueing it if new and under the caps; null = not captured.</summary>
    private string? EnqueuePage(Uri url, int depth)
    {
        if (_localPathByUrl.TryGetValue(Key(url), out var existing))
            return existing;
        if (_pages >= _options.MaxPages)
            return null;
        _pages++;
        return Track(url, DocType.Page, depth, LocalPathMapper.MapToLocalPath(url, isPage: true));
    }

    private string? EnqueueAsset(Uri url, int depth, DocType type)
    {
        if (_localPathByUrl.TryGetValue(Key(url), out var existing))
            return existing;
        if (_assets >= _options.MaxAssets)
            return null;
        _assets++;
        return Track(url, type, depth, LocalPathMapper.MapToLocalPath(url, isPage: false));
    }

    private string Track(Uri url, DocType type, int depth, string localPath)
    {
        // Two distinct URLs can map to one path (query-hash edge cases) — disambiguate deterministically.
        if (!_usedLocalPaths.Add(localPath))
        {
            var dot = localPath.LastIndexOf('.');
            var unique = $"_{(uint)Key(url).GetHashCode():x8}";
            localPath = dot > localPath.LastIndexOf('/') ? localPath.Insert(dot, unique) : localPath + unique;
            _usedLocalPaths.Add(localPath);
        }
        _localPathByUrl[Key(url)] = localPath;
        _queue.Enqueue((url, depth, type));
        return localPath;
    }

    private bool SameSite(Uri url)
    {
        var host = _rootHost;
        if (host == null)
            return false;
        return Bare(url.Host).Equals(Bare(host), StringComparison.OrdinalIgnoreCase);
        static string Bare(string h) => h.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? h[4..] : h;
    }

    private static string Key(Uri url) => url.AbsoluteUri;

    private static bool IsCss(string? mediaType) =>
        string.Equals(mediaType, "text/css", StringComparison.OrdinalIgnoreCase);
}
