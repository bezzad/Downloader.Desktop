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
}

/// <summary>One <c>#EXT-X-STREAM-INF</c> variant in a master playlist (resolved absolute <see cref="Uri"/>).</summary>
public sealed record HlsVariant(string Uri, long Bandwidth, string? Resolution);

/// <summary>A parsed master playlist: the available variants.</summary>
public sealed record HlsMasterPlaylist(IReadOnlyList<HlsVariant> Variants)
{
    /// <summary>Highest-BANDWIDTH variant (the default quality choice).</summary>
    public HlsVariant Best() =>
        Variants.OrderByDescending(v => v.Bandwidth).First();
}
