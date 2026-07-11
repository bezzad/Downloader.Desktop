using System.Text.Json;
using Downloader.Desktop.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Downloader.Desktop.Plugins.Hls;

/// <summary>
/// Resolves a link into a <see cref="DownloadPlan"/>. Two inputs are handled by one resolver:
/// <list type="bullet">
/// <item>a direct HLS (<c>.m3u8</c>) link → one <see cref="DownloadPart"/> per segment (+ optional init
/// segment) and a <see cref="PostProcessKind.Concat"/> recipe (segment order + any AES-128 key/IV);</item>
/// <item>a supported <b>site page URL</b> (e.g. an x.com status) → the real stream(s) extracted with
/// <see cref="IYtDlp"/>, then either fed back through the HLS segment pipeline (when the best format is
/// HLS) or turned into progressive / video+audio parts with an ffmpeg <see cref="PostProcessKind.Mux"/>.</item>
/// </list>
/// It never downloads — the host fetches the parts.
/// </summary>
public sealed class HlsResolver : ILinkResolver
{
    // Hosts whose page URLs are claimed for yt-dlp extraction. x.com / twitter.com are first-class (tested);
    // the rest are best-effort. Matching is host-only and network-free.
    private static readonly HashSet<string> SupportedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "x.com", "twitter.com",
        "youtube.com", "youtu.be", "m.youtube.com",
        "instagram.com", "tiktok.com", "facebook.com", "fb.watch",
        "vimeo.com", "dailymotion.com", "twitch.tv", "reddit.com", "streamable.com",
    };

    private readonly HttpClient _http;
    private readonly IM3u8Parser _parser;
    private readonly IContentTypeProbe? _probe;
    private readonly IYtDlp? _ytDlp;
    private readonly ILogger _log;

    public HlsResolver(
        HttpClient? http = null,
        IM3u8Parser? parser = null,
        IContentTypeProbe? probe = null,
        IYtDlp? ytDlp = null,
        ILogger? logger = null)
    {
        _http = http ?? new HttpClient();
        _parser = parser ?? new M3u8Parser();
        _probe = probe;
        _ytDlp = ytDlp;
        _log = logger ?? NullLogger.Instance;
    }

    public bool CanResolve(string url)
    {
        if (UrlLooksLikeHls(url)) return true;
        if (IsSupportedSite(url)) return true;
        return _probe?.LooksLikeHls(url) == true;
    }

    public Task<DownloadPlan> ResolveAsync(string url, CancellationToken cancellationToken)
        => ResolveAsync(url, options: null, cancellationToken);

    public async Task<DownloadPlan> ResolveAsync(string url, ResolveOptions options, CancellationToken cancellationToken)
    {
        // A direct media/playlist link is parsed straight away — yt-dlp is never invoked for it.
        if (!UrlLooksLikeHls(url) && IsSupportedSite(url))
            return await ResolveViaExtractionAsync(url, options?.CookieFilePath, options?.VariantId, cancellationToken)
                .ConfigureAwait(false);

        return await BuildHlsPlanAsync(url, SuggestFileName(url), headers: null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The selectable qualities (+ audio-only) behind a video page URL, so the host's Add window
    /// can offer a picker. Null for direct .m3u8 links (no yt-dlp involved) and non-extraction inputs.
    /// The extraction is cached so the subsequent resolve of the chosen variant doesn't re-run yt-dlp.</summary>
    public async Task<IReadOnlyList<LinkVariant>?> GetVariantsAsync(
        string url, ResolveOptions? options, CancellationToken cancellationToken)
    {
        if (UrlLooksLikeHls(url) || !IsSupportedSite(url) || _ytDlp is null)
            return null;

        var json = await ExtractCachedAsync(url, options?.CookieFilePath, cancellationToken).ConfigureAwait(false);
        var variants = SiteExtractor.ListVariants(json);
        return variants.Count > 0 ? variants : null;
    }

    // One extraction serves both the variant listing and the resolve that follows it (yt-dlp takes
    // 5–20 s). Short-lived on purpose: extracted stream URLs are signed/expiring, so anything beyond
    // bridging list→start within one Add flow must re-extract.
    private readonly object _cacheGate = new();
    private (string Url, bool HadCookies, string Json, DateTimeOffset At)? _lastExtraction;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private async Task<string> ExtractCachedAsync(string url, string? cookieFilePath, CancellationToken ct)
    {
        var hadCookies = !string.IsNullOrEmpty(cookieFilePath);
        lock (_cacheGate)
        {
            if (_lastExtraction is { } c && c.Url == url && c.HadCookies == hadCookies
                && DateTimeOffset.UtcNow - c.At < CacheTtl)
                return c.Json;
        }

        var json = await _ytDlp!.ExtractJsonAsync(url, cookieFilePath, ct).ConfigureAwait(false);
        lock (_cacheGate)
            _lastExtraction = (url, hadCookies, json, DateTimeOffset.UtcNow);
        return json;
    }

    /// <summary>Extract a site page URL with yt-dlp and build a plan from the chosen format(s).</summary>
    private async Task<DownloadPlan> ResolveViaExtractionAsync(
        string url, string? cookieFilePath, string? variantId, CancellationToken ct)
    {
        if (_ytDlp is null)
            throw new InvalidOperationException(
                "Video extraction is unavailable for this link (yt-dlp is not configured).");

        var json = await ExtractCachedAsync(url, cookieFilePath, ct).ConfigureAwait(false);
        var result = SiteExtractor.Select(json, variantId); // throws a clear message on no-media / bad JSON

        switch (result.Kind)
        {
            case ExtractionKind.Hls:
                _log.LogInformation("Extracted HLS stream from {Url} — reusing the segment pipeline", url);
                return await BuildHlsPlanAsync(result.PrimaryUrl!, result.FileName, result.Headers, ct)
                    .ConfigureAwait(false);

            case ExtractionKind.Progressive:
                _log.LogInformation("Extracted a progressive stream from {Url}", url);
                return new DownloadPlan
                {
                    SuggestedFileName = result.FileName,
                    Parts = new[]
                    {
                        new DownloadPart
                        {
                            Url = result.PrimaryUrl!,
                            Kind = PartKind.Combined,
                            Headers = result.Headers,
                            ExpectedSize = result.PrimarySize,
                        },
                    },
                    PostProcess = PostProcess.None,
                };

            case ExtractionKind.VideoAudio:
                _log.LogInformation("Extracted separate video+audio streams from {Url} — will mux", url);
                return new DownloadPlan
                {
                    SuggestedFileName = result.FileName,
                    Parts = new[]
                    {
                        new DownloadPart
                        {
                            Url = result.VideoUrl!, Kind = PartKind.Video,
                            Headers = result.Headers, ExpectedSize = result.VideoSize,
                        },
                        new DownloadPart
                        {
                            Url = result.AudioUrl!, Kind = PartKind.Audio,
                            Headers = result.Headers, ExpectedSize = result.AudioSize,
                        },
                    },
                    PostProcess = new PostProcess { Kind = PostProcessKind.Mux, Recipe = "video+audio" },
                };

            default:
                throw new InvalidOperationException("No downloadable video was found at this link.");
        }
    }

    /// <summary>Fetch and parse an HLS playlist (master → best variant → media) into a segment plan. Used
    /// for both direct <c>.m3u8</c> links and HLS streams extracted from a site page.</summary>
    private async Task<DownloadPlan> BuildHlsPlanAsync(
        string url, string suggestedName, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        var (content, baseUri) = await GetAsync(url, headers, ct).ConfigureAwait(false);

        if (_parser.IsMaster(content))
        {
            var master = _parser.ParseMaster(content, baseUri);
            var best = master.Best();
            _log.LogInformation("HLS master playlist: selected variant {Bandwidth} bps ({Resolution})",
                best.Bandwidth, best.Resolution ?? "?");
            (content, baseUri) = await GetAsync(best.Uri, headers, ct).ConfigureAwait(false);
        }

        var media = _parser.ParseMedia(content, baseUri);

        var parts = new List<DownloadPart>(media.Segments.Count + 1);
        if (media.InitSegmentUri is not null)
            parts.Add(new DownloadPart { Url = media.InitSegmentUri, Kind = PartKind.Segment, Headers = headers });
        foreach (var seg in media.Segments)
            parts.Add(new DownloadPart { Url = seg.Uri, Kind = PartKind.Segment, Headers = headers });

        var recipe = new ConcatRecipe
        {
            HasInitSegment = media.InitSegmentUri is not null,
            OutputExtension = ".mp4",
            Segments = media.Segments.Select(s => new SegmentEntry
            {
                Encrypted = s.Key is { } k && k.IsEncrypted,
                KeyUri = s.Key is { } k2 && k2.IsEncrypted ? k2.Uri : null,
                IvHex = s.Key?.Iv is { } iv ? Convert.ToHexString(iv) : null,
            }).ToList(),
        };

        _log.LogInformation("HLS resolved {Count} segments (encrypted: {Enc}) from {Url}",
            media.Segments.Count, media.IsEncrypted, url);

        return new DownloadPlan
        {
            SuggestedFileName = suggestedName,
            Parts = parts,
            PostProcess = new PostProcess
            {
                Kind = PostProcessKind.Concat,
                Recipe = JsonSerializer.Serialize(recipe),
            },
        };
    }

    internal static bool UrlLooksLikeHls(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        string path = url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var u)) path = u.AbsolutePath;
        int q = path.IndexOf('?');
        if (q >= 0) path = path[..q];
        return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Fast, network-free check: is this the page URL of a site yt-dlp can extract?</summary>
    internal static bool IsSupportedSite(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;

        var host = u.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host[4..];
        if (SupportedHosts.Contains(host)) return true;
        // Match subdomains of a supported host (e.g. mobile.twitter.com) without matching look-alikes.
        return SupportedHosts.Any(h => host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
    }

    internal static string SuggestFileName(string url)
    {
        string name = "video";
        if (Uri.TryCreate(url, UriKind.Absolute, out var u))
        {
            var last = u.Segments.LastOrDefault()?.Trim('/');
            if (!string.IsNullOrEmpty(last)) name = last;
        }
        int dot = name.LastIndexOf('.');
        if (dot > 0) name = name[..dot];
        if (string.IsNullOrWhiteSpace(name)) name = "video";
        return name + ".mp4";
    }

    private async Task<(string content, Uri baseUri)> GetAsync(
        string url, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (headers is not null)
            foreach (var (key, value) in headers)
                req.Headers.TryAddWithoutValidation(key, value);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var content = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var finalUri = resp.RequestMessage?.RequestUri ?? new Uri(url);
        return (content, finalUri);
    }
}
