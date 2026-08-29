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

    public SiteMediaResolver(IYtDlp ytDlp, ILogger? logger = null)
    {
        _ytDlp = ytDlp;
        _log = logger ?? NullLogger.Instance;
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
        lock (_cacheGate)
            _lastExtraction = (url, hadCookies, json, DateTimeOffset.UtcNow);
        return json;
    }

    /// <summary>What the user is told when a page's video exists only as an adaptive stream.</summary>
    internal const string AdaptiveOnlyMessage =
        "This page offers its video only as an adaptive stream, which this plugin can't assemble. "
        + "Install the \u201CStreaming media (HLS & DASH)\u201D plugin, which downloads those.";
}
