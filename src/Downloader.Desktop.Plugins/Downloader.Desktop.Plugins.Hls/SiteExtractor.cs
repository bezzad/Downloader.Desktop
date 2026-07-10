using System.Text.Json;

namespace Downloader.Desktop.Plugins.Hls;

internal enum ExtractionKind
{
    /// <summary>One progressive HTTP(S) file — a single part, no post-process.</summary>
    Progressive,
    /// <summary>An HLS (.m3u8) stream — reuse the segment pipeline (Concat).</summary>
    Hls,
    /// <summary>Separate best-video + best-audio streams — two parts, ffmpeg Mux.</summary>
    VideoAudio,
}

/// <summary>The chosen download shape after parsing <c>yt-dlp -J</c> — enough for the resolver to build a
/// <see cref="Downloader.Desktop.Plugins.DownloadPlan"/> without re-parsing the JSON.</summary>
internal sealed class ExtractionResult
{
    public required ExtractionKind Kind { get; init; }
    public required string FileName { get; init; }

    /// <summary>Progressive file URL or HLS playlist URL (for <see cref="ExtractionKind.Progressive"/>/<see cref="ExtractionKind.Hls"/>).</summary>
    public string? PrimaryUrl { get; init; }
    public long? PrimarySize { get; init; }

    /// <summary>Video-only / audio-only URLs (for <see cref="ExtractionKind.VideoAudio"/>).</summary>
    public string? VideoUrl { get; init; }
    public string? AudioUrl { get; init; }
    public long? VideoSize { get; init; }
    public long? AudioSize { get; init; }

    /// <summary>Request headers (cookies/referer/user-agent) yt-dlp says are needed to fetch the streams.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
}

/// <summary>
/// Turns raw <c>yt-dlp -J</c> JSON into an <see cref="ExtractionResult"/> using the D4 format-selection
/// policy: prefer a single progressive MP4 (simplest), else an HLS stream (reuse the segment pipeline),
/// else best-video + best-audio (ffmpeg mux). Pure/network-free so it is unit-tested against canned JSON.
/// </summary>
internal static class SiteExtractor
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static ExtractionResult Select(string json)
    {
        YtDlpInfo? info;
        try
        {
            info = JsonSerializer.Deserialize<YtDlpInfo>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Couldn't read the video information for this link.", ex);
        }

        if (info is null)
            throw new InvalidOperationException("No downloadable video was found at this link.");

        var name = SuggestName(info);
        var formats = info.Formats ?? new List<YtDlpFormat>();

        // 1) Single progressive HTTP(S) file with both audio+video — simplest, no post-process.
        // BUT only when it doesn't cap quality: YouTube's only combined progressive format is 360p while
        // separate video+audio streams go up to 1080p+ — preferring "simple" there downloads every video
        // in visibly poor quality. When a strictly taller video-only stream exists, fall through to mux.
        var progressive = formats
            .Where(f => !string.IsNullOrEmpty(f.Url)
                        && ((f.IsProgressiveHttp && f.HasVideo && f.HasAudio) || f.LikelyCombined))
            .OrderByDescending(f => f.Quality.height).ThenByDescending(f => f.Quality.tbr)
            .FirstOrDefault();
        var hasSplitAudio = formats.Any(f => !string.IsNullOrEmpty(f.Url) && f.HasAudio && !f.HasVideo && !f.IsHls);
        var tallestSplitVideo = !hasSplitAudio ? 0 : formats
            .Where(f => !string.IsNullOrEmpty(f.Url) && f.HasVideo && !f.HasAudio && !f.IsHls)
            .Select(f => f.Quality.height)
            .DefaultIfEmpty(0)
            .Max();
        if (progressive is not null && progressive.Quality.height >= tallestSplitVideo)
        {
            return new ExtractionResult
            {
                Kind = ExtractionKind.Progressive,
                FileName = EnsureExtension(name, progressive.Ext ?? "mp4"),
                PrimaryUrl = progressive.Url,
                PrimarySize = progressive.KnownSize,
                Headers = Merge(info.HttpHeaders, progressive.HttpHeaders),
            };
        }

        // 2) An HLS stream — reuse the existing segment pipeline.
        var hls = formats
            .Where(f => !string.IsNullOrEmpty(f.Url) && f.IsHls && f.HasVideo)
            .OrderByDescending(f => f.Quality.height).ThenByDescending(f => f.Quality.tbr)
            .FirstOrDefault()
            ?? formats.Where(f => !string.IsNullOrEmpty(f.Url) && f.IsHls)
                      .OrderByDescending(f => f.Quality.height).ThenByDescending(f => f.Quality.tbr)
                      .FirstOrDefault();
        // Top-level single-format HLS result (no formats array).
        if (hls is null && !string.IsNullOrEmpty(info.Url)
            && info.Protocol is not null && info.Protocol.Contains("m3u8", StringComparison.OrdinalIgnoreCase))
        {
            return new ExtractionResult
            {
                Kind = ExtractionKind.Hls,
                FileName = EnsureExtension(name, "mp4"),
                PrimaryUrl = info.Url,
                Headers = info.HttpHeaders,
            };
        }
        if (hls is not null)
        {
            return new ExtractionResult
            {
                Kind = ExtractionKind.Hls,
                FileName = EnsureExtension(name, "mp4"),
                PrimaryUrl = hls.Url,
                Headers = Merge(info.HttpHeaders, hls.HttpHeaders),
            };
        }

        // 3) Best video-only + best audio-only → ffmpeg mux. Honor yt-dlp's own pick when it gave one.
        var bestVideo = PickFrom(info.RequestedFormats, f => f.HasVideo && !f.HasAudio)
                        ?? formats.Where(f => !string.IsNullOrEmpty(f.Url) && f.HasVideo && !f.HasAudio && !f.IsHls)
                                  .OrderByDescending(f => f.Quality.height).ThenByDescending(f => f.Quality.tbr)
                                  .FirstOrDefault();
        var bestAudio = PickFrom(info.RequestedFormats, f => f.HasAudio && !f.HasVideo)
                        ?? formats.Where(f => !string.IsNullOrEmpty(f.Url) && f.HasAudio && !f.HasVideo && !f.IsHls)
                                  .OrderByDescending(f => f.Quality.tbr)
                                  .FirstOrDefault();
        if (bestVideo is not null && bestAudio is not null)
        {
            return new ExtractionResult
            {
                Kind = ExtractionKind.VideoAudio,
                FileName = EnsureExtension(name, "mp4"),
                VideoUrl = bestVideo.Url,
                AudioUrl = bestAudio.Url,
                VideoSize = bestVideo.KnownSize,
                AudioSize = bestAudio.KnownSize,
                Headers = Merge(info.HttpHeaders, Merge(bestVideo.HttpHeaders, bestAudio.HttpHeaders)),
            };
        }

        // A lone video-only progressive stream (rare) is still downloadable as a single file.
        var videoOnly = formats
            .Where(f => !string.IsNullOrEmpty(f.Url) && f.HasVideo && f.IsProgressiveHttp)
            .OrderByDescending(f => f.Quality.height).ThenByDescending(f => f.Quality.tbr)
            .FirstOrDefault();
        if (videoOnly is not null)
        {
            return new ExtractionResult
            {
                Kind = ExtractionKind.Progressive,
                FileName = EnsureExtension(name, videoOnly.Ext ?? "mp4"),
                PrimaryUrl = videoOnly.Url,
                PrimarySize = videoOnly.KnownSize,
                Headers = Merge(info.HttpHeaders, videoOnly.HttpHeaders),
            };
        }

        throw new InvalidOperationException("No downloadable video was found at this link.");
    }

    private static YtDlpFormat? PickFrom(List<YtDlpFormat>? formats, Func<YtDlpFormat, bool> predicate) =>
        formats?.FirstOrDefault(f => !string.IsNullOrEmpty(f.Url) && predicate(f));

    private static string SuggestName(YtDlpInfo info)
    {
        var title = info.Title;
        if (string.IsNullOrWhiteSpace(title)) title = info.Id;
        if (string.IsNullOrWhiteSpace(title)) title = "video";
        return Sanitize(title!);
    }

    // Strip a fixed cross-platform-unsafe set (Windows-reserved + this OS's invalid chars), so an extracted
    // name is safe even when the file later lands on / syncs to another OS.
    private static readonly HashSet<char> ReservedChars =
        new(Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }));

    private static string Sanitize(string name)
    {
        var cleaned = new string(name.Select(c => ReservedChars.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim();
        // Collapse whitespace runs and trim length so names stay filesystem-friendly.
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length > 120) cleaned = cleaned[..120].Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "video" : cleaned;
    }

    private static string EnsureExtension(string name, string ext)
    {
        ext = ext.TrimStart('.');
        if (string.IsNullOrWhiteSpace(ext) || ext.Equals("unknown_video", StringComparison.OrdinalIgnoreCase))
            ext = "mp4";
        return name.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase) ? name : $"{name}.{ext}";
    }

    private static IReadOnlyDictionary<string, string>? Merge(
        IReadOnlyDictionary<string, string>? a, IReadOnlyDictionary<string, string>? b)
    {
        if (a is null || a.Count == 0) return b is { Count: > 0 } ? b : null;
        if (b is null || b.Count == 0) return a;
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in a) merged[kv.Key] = kv.Value;
        foreach (var kv in b) merged[kv.Key] = kv.Value; // per-format headers win
        return merged;
    }
}
