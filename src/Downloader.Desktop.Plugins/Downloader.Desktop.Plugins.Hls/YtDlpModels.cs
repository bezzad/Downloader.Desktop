using System.Text.Json.Serialization;

namespace Downloader.Desktop.Plugins.Hls;

/// <summary>Top-level shape of <c>yt-dlp -J</c> output (only the fields the extractor uses).</summary>
internal sealed class YtDlpInfo
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("ext")] public string? Ext { get; set; }

    /// <summary>Present on single-format results (progressive) or as a fallback stream URL.</summary>
    [JsonPropertyName("url")] public string? Url { get; set; }

    /// <summary>Protocol for the top-level <see cref="Url"/> (e.g. <c>https</c>, <c>m3u8_native</c>).</summary>
    [JsonPropertyName("protocol")] public string? Protocol { get; set; }

    [JsonPropertyName("http_headers")] public Dictionary<string, string>? HttpHeaders { get; set; }

    /// <summary>All available formats (present for most extractors).</summary>
    [JsonPropertyName("formats")] public List<YtDlpFormat>? Formats { get; set; }

    /// <summary>yt-dlp's own pick (often a video-only + audio-only pair for DASH/adaptive sources).</summary>
    [JsonPropertyName("requested_formats")] public List<YtDlpFormat>? RequestedFormats { get; set; }
}

/// <summary>One entry of the yt-dlp <c>formats</c> array.</summary>
internal sealed class YtDlpFormat
{
    [JsonPropertyName("format_id")] public string? FormatId { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("ext")] public string? Ext { get; set; }
    [JsonPropertyName("protocol")] public string? Protocol { get; set; }
    [JsonPropertyName("vcodec")] public string? VCodec { get; set; }
    [JsonPropertyName("acodec")] public string? ACodec { get; set; }
    [JsonPropertyName("height")] public int? Height { get; set; }
    [JsonPropertyName("width")] public int? Width { get; set; }
    [JsonPropertyName("tbr")] public double? Tbr { get; set; }
    [JsonPropertyName("filesize")] public long? FileSize { get; set; }
    [JsonPropertyName("filesize_approx")] public long? FileSizeApprox { get; set; }
    [JsonPropertyName("http_headers")] public Dictionary<string, string>? HttpHeaders { get; set; }

    private static bool Present(string? codec) => !string.IsNullOrEmpty(codec) && codec != "none";

    [JsonIgnore] public bool HasVideo => Present(VCodec);
    [JsonIgnore] public bool HasAudio => Present(ACodec);

    /// <summary>Some extractors (e.g. x.com progressive MP4s) omit vcodec/acodec entirely. A plain HTTP
    /// format with video dimensions and no codec info is in practice a muxed video+audio file — treat it
    /// as a progressive candidate instead of skipping it (which used to force a video-only HLS pick).</summary>
    [JsonIgnore]
    public bool LikelyCombined =>
        VCodec is null && ACodec is null && (Height ?? 0) > 0 && IsProgressiveHttp;

    /// <summary>True when this format is an HLS stream (reuse the existing segment pipeline).</summary>
    [JsonIgnore]
    public bool IsHls =>
        (Protocol is not null && Protocol.Contains("m3u8", StringComparison.OrdinalIgnoreCase))
        || string.Equals(Ext, "m3u8", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when this format is DASH/segmented (must be muxed, not fetched as one file).</summary>
    [JsonIgnore]
    public bool IsDash =>
        Protocol is not null && Protocol.Contains("dash", StringComparison.OrdinalIgnoreCase);

    /// <summary>A single-file HTTP(S) stream the engine can fetch directly.</summary>
    [JsonIgnore]
    public bool IsProgressiveHttp =>
        !IsHls && !IsDash
        && (Protocol is null || Protocol.StartsWith("http", StringComparison.OrdinalIgnoreCase));

    [JsonIgnore] public long? KnownSize => FileSize ?? FileSizeApprox;

    /// <summary>Sort key: higher resolution, then higher bitrate.</summary>
    [JsonIgnore] public (int height, double tbr) Quality => (Height ?? 0, Tbr ?? 0);
}
