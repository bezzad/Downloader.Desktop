using System.Globalization;
using System.Text.Json;
using Downloader.Desktop.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Downloader.Desktop.Plugins.Hls.Dash;

/// <summary>
/// Resolves an MPEG-DASH (<c>.mpd</c>) link into a <see cref="DownloadPlan"/>. DASH keeps video and audio in
/// separate representations, so a plan is the chosen video stream's parts followed by the best audio
/// stream's parts, plus a <see cref="PostProcessKind.Concat"/> recipe that tells the post-processor where
/// one stream ends and the next begins — it concatenates each stream and muxes the two together.
/// The video representations are offered as quality variants; it never downloads anything itself.
/// </summary>
public sealed class DashResolver : ILinkResolver
{
    private readonly HttpClient _http;
    private readonly IMpdParser _parser;
    private readonly ILogger _log;

    private readonly object _cacheGate = new();
    private readonly Dictionary<string, (string Content, Uri BaseUri, DateTimeOffset At)> _manifestCache = new(StringComparer.Ordinal);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public DashResolver(HttpClient? http = null, IMpdParser? parser = null, ILogger? logger = null)
    {
        _http = http ?? new HttpClient();
        _parser = parser ?? new MpdParser();
        _log = logger ?? NullLogger.Instance;
    }

    public bool CanResolve(string url) => UrlLooksLikeDash(url);

    public Task<DownloadPlan> ResolveAsync(string url, CancellationToken cancellationToken)
        => ResolveAsync(url, options: null!, cancellationToken);

    public async Task<DownloadPlan> ResolveAsync(
        string url, ResolveOptions options, CancellationToken cancellationToken)
    {
        var headers = options?.Headers;
        var manifest = await LoadAsync(url, headers, cancellationToken).ConfigureAwait(false);

        var video = Pick(manifest, options?.VariantId);
        var audio = manifest.BestAudio();
        if (video is null && audio is null)
            throw new DashException("This DASH manifest contains no audio or video to download.");

        var streams = new[] { video, audio }.Where(r => r is not null).Select(r => r!).ToList();

        var parts = new List<DownloadPart>();
        var groups = new List<StreamGroup>();
        foreach (var stream in streams)
        {
            if (stream.InitSegmentUri is not null)
                parts.Add(PartFor(stream.InitSegmentUri, stream, headers));
            foreach (var segment in stream.SegmentUris)
                parts.Add(PartFor(segment, stream, headers));

            groups.Add(new StreamGroup
            {
                HasInitSegment = stream.InitSegmentUri is not null,
                SegmentCount = stream.SegmentUris.Count,
            });
        }

        var recipe = new ConcatRecipe
        {
            OutputExtension = ".mp4",
            // DASH segments are fMP4, not MPEG-TS: the intermediate must not claim to be a transport stream.
            IntermediateExtension = ".mp4",
            Streams = groups,
            // No AES-128 here — a DASH manifest that is encrypted at all is refused as DRM by the parser.
            Segments = Enumerable.Range(0, groups.Sum(g => g.SegmentCount))
                .Select(_ => new SegmentEntry())
                .ToList(),
        };

        _log.LogInformation(
            "DASH resolved {Streams} stream(s), {Parts} parts (video: {Video}, audio: {Audio}) from {Url}",
            streams.Count, parts.Count, video?.Id ?? "none", audio?.Id ?? "none", url);

        return new DownloadPlan
        {
            SuggestedFileName = SuggestFileName(url),
            Parts = parts,
            PostProcess = new PostProcess
            {
                Kind = PostProcessKind.Concat,
                Recipe = JsonSerializer.Serialize(recipe),
            },
        };
    }

    /// <summary>The video qualities in the manifest, so the Add window can offer a picker. Null when the
    /// manifest offers no choice (one video representation, or none at all) — the resolve then just takes
    /// what is there. The manifest fetch is cached, so the resolve that follows does not re-download it.</summary>
    public async Task<IReadOnlyList<LinkVariant>?> GetVariantsAsync(
        string url, ResolveOptions? options, CancellationToken cancellationToken)
    {
        if (!CanResolve(url))
            return null;

        var manifest = await LoadAsync(url, options?.Headers, cancellationToken).ConfigureAwait(false);
        var variants = ListVariants(manifest);
        return variants.Count > 1 ? variants : null;
    }

    // ── pure helpers (unit-tested) ──────────────────────────────────────────────────────────────────

    internal static IReadOnlyList<LinkVariant> ListVariants(DashManifest manifest)
    {
        var audioBandwidth = manifest.BestAudio()?.Bandwidth ?? 0;

        return manifest.Video
            .OrderByDescending(r => r.Bandwidth)
            .Select((r, i) =>
            {
                var size = SizeOf(r.Bandwidth + audioBandwidth, manifest.DurationSeconds);
                return new LinkVariant
                {
                    Id = r.Id,
                    Label = LabelOf(r, size),
                    Description = r.Codecs,
                    ExpectedSize = size,
                    IsDefault = i == 0,
                };
            })
            .ToList();
    }

    /// <summary>The chosen video representation, or the highest-bandwidth one when no (or an unknown)
    /// variant was picked — an unknown id must not fail the download, it falls back to the default.</summary>
    internal static DashRepresentation? Pick(DashManifest manifest, string? variantId)
    {
        if (!string.IsNullOrEmpty(variantId))
        {
            var chosen = manifest.Video.FirstOrDefault(r => string.Equals(r.Id, variantId, StringComparison.Ordinal));
            if (chosen is not null) return chosen;
        }
        return manifest.BestVideo();
    }

    internal static bool UrlLooksLikeDash(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        string path = url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var u)) path = u.AbsolutePath;
        int q = path.IndexOf('?');
        if (q >= 0) path = path[..q];
        return path.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase);
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

    // ── internals ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>A whole-file representation is downloaded as one ordinary (multi-chunk) part; a segmented one
    /// yields many small <see cref="PartKind.Segment"/> parts, which the host fetches in parallel.</summary>
    private static DownloadPart PartFor(
        string url, DashRepresentation stream, IReadOnlyDictionary<string, string>? headers) =>
        new()
        {
            Url = url,
            Kind = stream.IsSingleFile
                ? stream.Kind == DashStreamKind.Audio ? PartKind.Audio : PartKind.Video
                : PartKind.Segment,
            Headers = headers,
        };

    private async Task<DashManifest> LoadAsync(
        string url, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        var (content, baseUri) = await GetAsync(url, headers, ct).ConfigureAwait(false);
        return _parser.Parse(content, baseUri);
    }

    private async Task<(string content, Uri baseUri)> GetAsync(
        string url, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        lock (_cacheGate)
        {
            if (_manifestCache.TryGetValue(url, out var hit) && DateTimeOffset.UtcNow - hit.At < CacheTtl)
                return (hit.Content, hit.BaseUri);
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (headers is not null)
            foreach (var (key, value) in headers)
                req.Headers.TryAddWithoutValidation(key, value);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var content = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        // Relative URLs resolve against the FINAL uri, after any redirect.
        var finalUri = resp.RequestMessage?.RequestUri ?? new Uri(url);

        lock (_cacheGate)
            _manifestCache[url] = (content, finalUri, DateTimeOffset.UtcNow);
        return (content, finalUri);
    }

    private static long? SizeOf(long bitsPerSecond, double durationSeconds)
    {
        if (bitsPerSecond <= 0 || durationSeconds <= 0) return null;
        return (long)(bitsPerSecond / 8.0 * durationSeconds);
    }

    private static string LabelOf(DashRepresentation r, long? size)
    {
        var quality = r.Height > 0
            ? r.Height.ToString(CultureInfo.InvariantCulture) + "p"
            : r.Bandwidth > 0
                ? (r.Bandwidth / 1000).ToString(CultureInfo.InvariantCulture) + " kbps"
                : "Video";
        return size is { } s ? $"{quality} (≈{FormatSize(s)})" : quality;
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.#} GB"
        : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0} MB"
        : $"{bytes / (double)(1L << 10):0} KB";
}
