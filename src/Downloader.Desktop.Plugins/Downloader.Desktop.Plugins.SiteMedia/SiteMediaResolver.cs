using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Downloader.Desktop.Plugins.SiteMedia;

/// <summary>
/// Turns a supported site's page URL (a video page, a post) into a <see cref="DownloadPlan"/>: the page is
/// extracted with <see cref="IYtDlp"/>, and the chosen format becomes either one progressive part or a
/// video+audio pair for ffmpeg to mux. It never downloads anything itself; the host fetches the parts.
/// <para>
/// A page whose media is offered ONLY as an adaptive stream fails with a message saying so rather than
/// downloading a playlist file: assembling segments is the streaming-media plugin's job, and duplicating
/// its pipeline here would make two independently-installed plugins ship the same code twice.
/// </para>
/// </summary>
public sealed class SiteMediaResolver : ILinkResolver
{
    // Hosts whose page URLs are claimed for extraction. Host-only and network-free, so a claim costs
    // nothing. This is deliberately a list rather than "claim everything": a fallback that swallowed every
    // URL would shadow the direct-link, GitHub and Ollama resolvers.
    private static readonly HashSet<string> SupportedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com", "youtu.be", "m.youtube.com", "music.youtube.com",
        "x.com", "twitter.com",
        "instagram.com", "tiktok.com", "facebook.com", "fb.watch",
        "vimeo.com", "dailymotion.com", "twitch.tv", "reddit.com", "streamable.com",
        "soundcloud.com", "bilibili.com", "odysee.com", "rumble.com",
    };

    private readonly IYtDlp _ytDlp;
    private readonly ILogger _log;
    private readonly IMediaProbe _probe;

    public SiteMediaResolver(IYtDlp ytDlp, ILogger? logger = null)
        : this(ytDlp, logger, probe: null) { }

    internal SiteMediaResolver(IYtDlp ytDlp, ILogger? logger, IMediaProbe? probe)
    {
        _ytDlp = ytDlp;
        _log = logger ?? NullLogger.Instance;
        _probe = probe ?? new HttpMediaProbe();
    }

    /// <summary>True when <paramref name="url"/> is a page on a site this plugin extracts. Pure: no
    /// network, so the host can ask this on every keystroke in the Add window.</summary>
    public bool CanResolve(string url) => IsSupportedSite(url);

    internal static bool IsSupportedSite(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        var host = uri.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host[4..];
        if (SupportedHosts.Contains(host)) return true;
        // A subdomain of a supported host (m.facebook.com, vm.tiktok.com) — never a look-alike suffix.
        return SupportedHosts.Any(h => host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
    }

    public Task<DownloadPlan> ResolveAsync(string url, CancellationToken cancellationToken)
        => ResolveAsync(url, options: null, cancellationToken);

    public async Task<DownloadPlan> ResolveAsync(string url, ResolveOptions? options, CancellationToken cancellationToken)
    {
        var json = await ExtractCachedAsync(url, options?.CookieFilePath, cancellationToken).ConfigureAwait(false);
        var result = SiteExtractor.Select(json, options?.VariantId); // throws a clear message on no-media
        result = await EnsureFetchableAsync(url, options, result, cancellationToken).ConfigureAwait(false);

        switch (result.Kind)
        {
            case ExtractionKind.Hls:
                _log.LogInformation("{Url} offers only an adaptive stream — not downloadable here", url);
                throw new InvalidOperationException(AdaptiveOnlyMessage);

            case ExtractionKind.Progressive:
                _log.LogInformation("Extracted a single media file from {Url}", url);
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

    /// <summary>The qualities behind a page URL, so the Add window can offer a picker. Null when the page
    /// offers no real choice. The extraction is cached so the resolve that follows doesn't re-run the
    /// tool.</summary>
    public async Task<IReadOnlyList<LinkVariant>?> GetVariantsAsync(
        string url, ResolveOptions? options, CancellationToken cancellationToken)
    {
        if (!IsSupportedSite(url)) return null;

        var json = await ExtractCachedAsync(url, options?.CookieFilePath, cancellationToken).ConfigureAwait(false);
        var variants = SiteExtractor.ListVariants(json);
        return variants.Count > 0 ? variants : null;
    }

    /// <summary>
    /// What is tried, in order, when the chosen stream URL is refused — each entry is a player client to
    /// pin, and <c>null</c> means "let yt-dlp choose".
    /// <para>
    /// The FIRST retry deliberately drops the session (see <see cref="RetryWithoutCookies"/>): handing
    /// yt-dlp a signed-in session is what makes YouTube answer through its web clients, whose links its
    /// CDN then serves only against a GVS PO token this app cannot mint — yt-dlp says so itself
    /// ("&lt;client&gt; client https formats require a GVS PO Token which was not provided … may yield
    /// HTTP Error 403"). Extracted anonymously the same public video comes back through a client whose
    /// links are served. The pinned clients follow for the cases where the session is what got us in.
    /// </para>
    /// </summary>
    internal static readonly string?[] YouTubeRetryClients = { RetryWithoutCookies, "tv_simply", "web_safari" };

    /// <summary>The retry-list entry meaning "extract again, but anonymously".</summary>
    internal const string? RetryWithoutCookies = null;

    /// <summary>What the user is told when every attempt's links are refused.</summary>
    internal const string AllRefusedMessage =
        "YouTube refused every download link it offered for this video (HTTP 403). It does that when it "
        + "will not serve the video without a signed-in session AND demands a token this app cannot "
        + "produce. Try again later, or try a video that plays without signing in.";

    /// <summary>
    /// Confirms the chosen stream is actually fetchable and, when it is refused outright, re-extracts the
    /// page through another player client and takes the first choice that is not.
    /// <para>
    /// Without this, an extraction that "succeeded" produced a plan whose very first request came back
    /// 403 — the row failed a second after starting with a raw HTTP status and nothing the user could act
    /// on (issue: YouTube downloads failing with "403 (Forbidden)"). A probe that cannot reach the server
    /// at all never rejects anything: only a refusal counts.
    /// </para>
    /// </summary>
    private async Task<ExtractionResult> EnsureFetchableAsync(
        string url, ResolveOptions? options, ExtractionResult result, CancellationToken ct)
    {
        // An HLS/adaptive result is refused by the resolver itself a moment later, and a result with no
        // direct stream has nothing to probe.
        if (result.Kind is not (ExtractionKind.Progressive or ExtractionKind.VideoAudio))
            return result;

        // Only YouTube offers another way to ask. Probing anywhere else would spend a request on a
        // question nothing could act on — the download would go ahead and report the site's refusal
        // itself, exactly as it does today.
        if (!IsYouTube(url))
            return result;

        // Ok, or a probe that could not reach the server at all: nothing to second-guess.
        if (await ProbeAsync(result, ct).ConfigureAwait(false) != ProbeVerdict.Refused)
            return result;

        foreach (var client in YouTubeRetryClients)
        {
            ct.ThrowIfCancellationRequested();
            // Dropping the session is only worth trying when there WAS one; without cookies the first
            // attempt already was the anonymous one.
            var anonymous = client == RetryWithoutCookies;
            if (anonymous && string.IsNullOrEmpty(options?.CookieFilePath))
                continue;

            var cookieFile = anonymous ? null : options?.CookieFilePath;
            var attempt = anonymous ? "without the session" : $"the {client} player client";
            try
            {
                var json = await _ytDlp
                    .ExtractJsonAsync(url, cookieFile, client, ct)
                    .ConfigureAwait(false);
                var retried = SiteExtractor.Select(json, options?.VariantId);
                if (await ProbeAsync(retried, ct).ConfigureAwait(false) == ProbeVerdict.Refused)
                {
                    _log.LogWarning("Extracting {Attempt} gives refused links too", attempt);
                    continue;
                }

                _log.LogInformation("Using the extraction {Attempt} — its links are served", attempt);
                StoreExtraction(url, cookieFile, json);
                return retried;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Re-extracting {Attempt} failed", attempt);
            }
        }

        throw new InvalidOperationException(AllRefusedMessage);
    }

    private Task<ProbeVerdict> ProbeAsync(ExtractionResult result, CancellationToken ct)
        => _probe.CheckAsync(result.VideoUrl ?? result.PrimaryUrl ?? "", result.Headers, ct);

    /// <summary>True for a YouTube page (incl. youtu.be and the m./music. hosts), never a look-alike.</summary>
    internal static bool IsYouTube(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host[4..];
        return host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase);
    }

    // One extraction serves both the variant listing and the resolve that follows it (the tool takes
    // 5–20 s). Short-lived on purpose: extracted stream URLs are signed and expiring, so anything beyond
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

        var json = await _ytDlp.ExtractJsonAsync(url, cookieFilePath, ct).ConfigureAwait(false);
        StoreExtraction(url, cookieFilePath, json);
        return json;
    }

    /// <summary>Makes an extraction the cached one for this link — including a retry through another
    /// player client, so the choice that actually works is the one a following resolve reuses.</summary>
    private void StoreExtraction(string url, string? cookieFilePath, string json)
    {
        lock (_cacheGate)
            _lastExtraction = (url, !string.IsNullOrEmpty(cookieFilePath), json, DateTimeOffset.UtcNow);
    }

    /// <summary>What the user is told when a page's video exists only as an adaptive stream.</summary>
    internal const string AdaptiveOnlyMessage =
        "This page offers its video only as an adaptive stream, which this plugin can't assemble. "
        + "Install the \u201CStreaming media (HLS & DASH)\u201D plugin, which downloads those.";
}
