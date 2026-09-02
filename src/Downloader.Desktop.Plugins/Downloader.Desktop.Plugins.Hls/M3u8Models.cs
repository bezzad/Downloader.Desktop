namespace Downloader.Desktop.Plugins.Hls;

/// <summary>An <c>#EXT-X-KEY</c> entry. <see cref="Method"/> is "NONE" or "AES-128".</summary>
public sealed record HlsKey(string Method, string Uri, byte[]? Iv)
{
    public bool IsEncrypted => !string.Equals(Method, "NONE", StringComparison.OrdinalIgnoreCase)
                               && !string.IsNullOrEmpty(Uri);
}

/// <summary>One media segment (resolved absolute <see cref="Uri"/>), its duration and the key that applies.</summary>
public sealed record HlsSegment(string Uri, double Duration, HlsKey? Key, long MediaSequence);

/// <summary>A parsed media playlist: ordered segments + optional <c>#EXT-X-MAP</c> init segment.</summary>
public sealed record HlsMediaPlaylist(
    IReadOnlyList<HlsSegment> Segments,
    string? InitSegmentUri)
{
    public bool IsEncrypted => Segments.Any(s => s.Key is { } k && k.IsEncrypted);
    /// <summary>Sum of <c>#EXTINF</c> durations, used to estimate variant size from bandwidth.</summary>
    public double Duration => Segments.Sum(s => s.Duration);
}

/// <summary>One <c>#EXT-X-STREAM-INF</c> variant in a master playlist (resolved absolute <see cref="Uri"/>).
/// <paramref name="AudioGroupId"/> is its <c>AUDIO</c> attribute: when set, the variant's own playlist
/// carries video only and the audio lives in a separate rendition of that group.</summary>
public sealed record HlsVariant(
    string Uri, long Bandwidth, string? Resolution, string? AudioGroupId = null, string? Codecs = null);

/// <summary>
/// One <c>#EXT-X-MEDIA</c> rendition. Only <c>TYPE=AUDIO</c> entries with a <c>URI</c> are actionable here:
/// they are the separate audio track a video-only variant needs in order to have sound.
/// </summary>
public sealed record HlsRendition(
    string Type, string GroupId, string? Uri, string? Name, string? Language, bool IsDefault);

/// <summary>A parsed master playlist: the available variants plus any <c>#EXT-X-MEDIA</c> renditions.</summary>
public sealed record HlsMasterPlaylist(
    IReadOnlyList<HlsVariant> Variants,
    IReadOnlyList<HlsRendition> Renditions)
{
    public HlsMasterPlaylist(IReadOnlyList<HlsVariant> variants)
        : this(variants, Array.Empty<HlsRendition>()) { }

    /// <summary>Highest-BANDWIDTH variant (the default quality choice).</summary>
    public HlsVariant Best() =>
        Variants.OrderByDescending(v => v.Bandwidth).First();

    /// <summary>
    /// The audio rendition that must be downloaded alongside <paramref name="variant"/>, or null when the
    /// variant already carries its own audio. Matched by the variant's <c>AUDIO</c> group, preferring the
    /// group's <c>DEFAULT=YES</c> entry. When the variant names no group but its <c>CODECS</c> list proves
    /// it has no audio codec, the master's default audio rendition is used — some masters omit the
    /// attribute even though the audio genuinely lives elsewhere.
    /// </summary>
    public HlsRendition? AudioFor(HlsVariant variant)
    {
        var audio = Renditions
            .Where(r => string.Equals(r.Type, "AUDIO", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(r.Uri))
            .ToList();
        if (audio.Count == 0) return null;

        if (!string.IsNullOrEmpty(variant.AudioGroupId))
            audio = audio.Where(r => string.Equals(r.GroupId, variant.AudioGroupId, StringComparison.Ordinal)).ToList();
        else if (!DeclaresNoAudio(variant.Codecs))
            return null;

        return audio.FirstOrDefault(r => r.IsDefault) ?? audio.FirstOrDefault();
    }

    /// <summary>True when a CODECS attribute is present and lists no audio codec — i.e. the variant is
    /// provably video-only. An absent/empty CODECS says nothing, so it is not treated as proof.</summary>
    internal static bool DeclaresNoAudio(string? codecs)
    {
        if (string.IsNullOrWhiteSpace(codecs)) return false;
        string[] audioCodecs = ["mp4a", "ac-3", "ec-3", "opus", "vorbis", "alac", "flac", "dts"];
        return !codecs.Split(',')
            .Select(c => c.Trim())
            .Any(c => audioCodecs.Any(a => c.StartsWith(a, StringComparison.OrdinalIgnoreCase)));
    }
}
