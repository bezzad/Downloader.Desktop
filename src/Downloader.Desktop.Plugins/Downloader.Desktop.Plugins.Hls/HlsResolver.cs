using System.Globalization;
using System.Text.Json;
using Downloader.Desktop.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Downloader.Desktop.Plugins.Hls;

/// <summary>
/// Resolves a direct HLS (<c>.m3u8</c>) link into a <see cref="DownloadPlan"/>: one
/// <see cref="DownloadPart"/> per segment (+ optional init segment) and a
/// <see cref="PostProcessKind.Concat"/> recipe (segment order + any AES-128 key/IV).
/// Master playlists expose their <c>#EXT-X-STREAM-INF</c> renditions as quality variants
/// so the Add window can offer a picker; the default pick is the highest-bandwidth stream.
/// It never downloads — the host fetches the parts.
/// </summary>
public sealed class HlsResolver : ILinkResolver
{
    private readonly HttpClient _http;
    private readonly IM3u8Parser _parser;
    private readonly IContentTypeProbe? _probe;
    private readonly ILogger _log;

    public HlsResolver(
        HttpClient? http = null,
        IM3u8Parser? parser = null,
        IContentTypeProbe? probe = null,
        ILogger? logger = null)
    {
        _http = http ?? new HttpClient();
        _parser = parser ?? new M3u8Parser();
        _probe = probe;
        _log = logger ?? NullLogger.Instance;
    }

    public bool CanResolve(string url)
    {
        if (UrlLooksLikeHls(url)) return true;
        return _probe?.LooksLikeHls(url) == true;
    }

    public Task<DownloadPlan> ResolveAsync(string url, CancellationToken cancellationToken)
        => ResolveAsync(url, options: null, cancellationToken);

    /// <summary>The SDK declares <paramref name="options"/> non-nullable, but it is optional in practice;
    /// widening a parameter to accept null is legal for an implementation, and honest here.</summary>
    public Task<DownloadPlan> ResolveAsync(string url, ResolveOptions? options, CancellationToken cancellationToken)
        => BuildHlsPlanAsync(url, SuggestFileName(url), options?.Headers, options?.VariantId, cancellationToken);

    /// <summary>The selectable qualities in a master playlist, so the host's Add window can offer a
    /// picker. Null for a media playlist (one rendition, no choice) and for non-HLS inputs.
    /// Playlist fetches are cached so the subsequent resolve of the chosen variant doesn't re-download
    /// the master / default media playlist.</summary>
    public async Task<IReadOnlyList<LinkVariant>?> GetVariantsAsync(
        string url, ResolveOptions? options, CancellationToken cancellationToken)
    {
        if (!CanResolve(url))
            return null;

        var (content, baseUri) = await GetAsync(url, options?.Headers, cancellationToken).ConfigureAwait(false);
        if (!_parser.IsMaster(content))
            return null;

        var master = _parser.ParseMaster(content, baseUri);
        var duration = await TryDurationAsync(master.Best().Uri, options?.Headers, cancellationToken).ConfigureAwait(false);
        var variants = ListMasterVariants(master, duration);
        return variants.Count > 0 ? variants : null;
    }

    private readonly object _cacheGate = new();
    private readonly Dictionary<string, (string Content, Uri BaseUri, DateTimeOffset At)> _playlistCache = new(StringComparer.Ordinal);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>Fetch and parse an HLS playlist (master → chosen/best variant → media) into a segment plan.</summary>
    private async Task<DownloadPlan> BuildHlsPlanAsync(
        string url, string suggestedName, IReadOnlyDictionary<string, string>? headers,
        string? variantId, CancellationToken ct)
    {
        var (content, baseUri) = await GetAsync(url, headers, ct).ConfigureAwait(false);

        HlsMediaPlaylist? audio = null;
        if (_parser.IsMaster(content))
        {
            var master = _parser.ParseMaster(content, baseUri);
            var chosen = Pick(master, variantId);
            _log.LogInformation("HLS master playlist: selected variant {Bandwidth} bps ({Resolution})",
                chosen.Bandwidth, chosen.Resolution ?? "?");
            (content, baseUri) = await GetAsync(chosen.Uri, headers, ct).ConfigureAwait(false);

            // A variant that points at an #EXT-X-MEDIA audio group carries no audio of its own: without
            // downloading that rendition too, the assembled file plays but is silent.
            if (master.AudioFor(chosen) is { Uri: { } audioUrl } rendition)
            {
                var (audioContent, audioBase) = await GetAsync(audioUrl, headers, ct).ConfigureAwait(false);
                audio = _parser.ParseMedia(audioContent, audioBase);
                _log.LogInformation("HLS variant has a separate audio rendition ({Name}, {Segments} segments)",
                    rendition.Name ?? rendition.GroupId, audio.Segments.Count);
            }
        }

        var media = _parser.ParseMedia(content, baseUri);

        var streams = audio is null
            ? new List<HlsMediaPlaylist> { media }
            : new List<HlsMediaPlaylist> { media, audio };

        var parts = new List<DownloadPart>(streams.Sum(s => s.Segments.Count + 1));
        foreach (var stream in streams)
        {
            if (stream.InitSegmentUri is not null)
                parts.Add(new DownloadPart { Url = stream.InitSegmentUri, Kind = PartKind.Segment, Headers = headers });
            foreach (var seg in stream.Segments)
                parts.Add(new DownloadPart { Url = seg.Uri, Kind = PartKind.Segment, Headers = headers });
        }

        var recipe = new ConcatRecipe
        {
            HasInitSegment = media.InitSegmentUri is not null,
            OutputExtension = ".mp4",
            // fMP4 segments (an #EXT-X-MAP init segment) must not be handed to ffmpeg as ".ts" — the
            // wrong hint throws its container probing off.
            IntermediateExtension = media.InitSegmentUri is not null ? ".mp4" : ".ts",
            // One group per stream, so the post-processor concatenates video and audio separately and then
            // muxes them. Absent (null) for a single stream, exactly as every earlier recipe.
            Streams = audio is null
                ? null
                : streams.Select(s => new StreamGroup
                {
                    HasInitSegment = s.InitSegmentUri is not null,
                    SegmentCount = s.Segments.Count,
                }).ToList(),
            Segments = streams.SelectMany(s => s.Segments).Select(s => new SegmentEntry
            {
                Encrypted = s.Key is { } k && k.IsEncrypted,
                KeyUri = s.Key is { } k2 && k2.IsEncrypted ? k2.Uri : null,
                IvHex = s.Key?.Iv is { } iv ? Convert.ToHexString(iv) : null,
            }).ToList(),
            // The key is fetched at assembly time, by the post-processor, out of a client that knows nothing
            // about this download — so its context has to travel on the recipe. The key is normally served
            // from the same protected origin as the playlist we just fetched with these very headers.
            KeyHeaders = headers is { Count: > 0 } && streams.Any(st => st.IsEncrypted)
                ? headers.ToDictionary(h => h.Key, h => h.Value, StringComparer.OrdinalIgnoreCase)
                : null,
        };

        _log.LogInformation("HLS resolved {Count} segments across {Streams} stream(s) (encrypted: {Enc}) from {Url}",
            streams.Sum(st => st.Segments.Count), streams.Count, streams.Any(st => st.IsEncrypted), url);

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

    internal static IReadOnlyList<LinkVariant> ListMasterVariants(HlsMasterPlaylist master, double durationSeconds)
    {
        var ordered = master.Variants
            .Select((v, i) => (v, i))
            .OrderByDescending(t => t.v.Bandwidth)
            .ToList();

        var used = new HashSet<string>(StringComparer.Ordinal);
        var variants = new List<LinkVariant>(ordered.Count);
        foreach (var (v, i) in ordered)
        {
            var id = UniqueId(v, i, used);
            var size = SizeOf(v.Bandwidth, durationSeconds);
            variants.Add(new LinkVariant
            {
                Id = id,
                Label = LabelOf(v, size),
                ExpectedSize = size,
                IsDefault = variants.Count == 0,
            });
        }
        return variants;
    }

    internal static HlsVariant Pick(HlsMasterPlaylist master, string? variantId)
    {
        if (string.IsNullOrEmpty(variantId))
            return master.Best();

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (v, i) in master.Variants.Select((v, i) => (v, i)).OrderByDescending(t => t.v.Bandwidth))
        {
            if (string.Equals(UniqueId(v, i, used), variantId, StringComparison.Ordinal))
                return v;
        }
        return master.Best();
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

    private async Task<double> TryDurationAsync(
        string mediaUrl, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        try
        {
            var (content, baseUri) = await GetAsync(mediaUrl, headers, ct).ConfigureAwait(false);
            if (_parser.IsMaster(content)) return 0;
            return _parser.ParseMedia(content, baseUri).Duration;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogDebug(ex, "Couldn't read media playlist duration for size estimate");
            return 0;
        }
    }

    private async Task<(string content, Uri baseUri)> GetAsync(
        string url, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        lock (_cacheGate)
        {
            if (_playlistCache.TryGetValue(url, out var hit) && DateTimeOffset.UtcNow - hit.At < CacheTtl)
                return (hit.Content, hit.BaseUri);
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (headers is not null)
            foreach (var (key, value) in headers)
                req.Headers.TryAddWithoutValidation(key, value);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var content = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var finalUri = resp.RequestMessage?.RequestUri ?? new Uri(url);

        lock (_cacheGate)
            _playlistCache[url] = (content, finalUri, DateTimeOffset.UtcNow);
        return (content, finalUri);
    }

    private static string UniqueId(HlsVariant v, int index, HashSet<string> used)
    {
        var id = v.Bandwidth > 0
            ? v.Bandwidth.ToString(CultureInfo.InvariantCulture)
            : "v" + index.ToString(CultureInfo.InvariantCulture);
        if (!used.Add(id))
        {
            id = id + "-" + index.ToString(CultureInfo.InvariantCulture);
            used.Add(id);
        }
        return id;
    }

    private static long? SizeOf(long bandwidth, double durationSeconds)
    {
        if (bandwidth <= 0 || durationSeconds <= 0) return null;
        return (long)(bandwidth / 8.0 * durationSeconds);
    }

    private static string LabelOf(HlsVariant v, long? size)
    {
        var height = HeightOf(v.Resolution);
        var quality = height > 0
            ? height.ToString(CultureInfo.InvariantCulture) + "p"
            : v.Bandwidth > 0
                ? (v.Bandwidth / 1000).ToString(CultureInfo.InvariantCulture) + " kbps"
                : "Video";
        return size is { } s ? $"{quality} (≈{FormatSize(s)})" : quality;
    }

    private static int HeightOf(string? resolution)
    {
        if (string.IsNullOrEmpty(resolution)) return 0;
        var x = resolution.LastIndexOf('x');
        if (x < 0 || x == resolution.Length - 1) return 0;
        return int.TryParse(resolution[(x + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)
            ? h : 0;
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.#} GB"
        : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0} MB"
        : $"{bytes / (double)(1L << 10):0} KB";
}
