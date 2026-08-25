namespace Downloader.Desktop.Plugins.Hls;

/// <summary>
/// The Concat post-process recipe, serialized into <c>PostProcess.Recipe</c> as JSON by the resolver and
/// read back by the post-processor. It is self-contained: given the downloaded segment files (in plan
/// order) plus this recipe, the processor can decrypt + concatenate without any shared in-memory state.
/// </summary>
public sealed class ConcatRecipe
{
    /// <summary>When true, the first downloaded input file is the <c>#EXT-X-MAP</c> init segment and is
    /// prepended verbatim (not counted in <see cref="Segments"/>).</summary>
    public bool HasInitSegment { get; set; }

    /// <summary>Suggested output container extension (".mp4" / ".ts").</summary>
    public string OutputExtension { get; set; } = ".mp4";

    /// <summary>
    /// Extension of the intermediate concatenated file handed to ffmpeg. MPEG-TS (HLS's usual payload) by
    /// default; DASH sets ".mp4" because its segments are fMP4 and mislabelling them can throw ffmpeg's
    /// container probing off.
    /// </summary>
    public string IntermediateExtension { get; set; } = ".ts";

    /// <summary>
    /// How the downloaded files split into separate elementary streams, in part order. Null (the HLS case)
    /// means one stream described by <see cref="HasInitSegment"/> and <see cref="Segments"/>, which is how
    /// every recipe written before DASH support deserializes. Two groups — video then audio, as DASH
    /// delivers them — are concatenated separately and then muxed into one file.
    /// </summary>
    public List<StreamGroup>? Streams { get; set; }

    /// <summary>One entry per media segment, in part order across ALL streams (maps 1:1 to the downloaded
    /// segment files, init segments excluded).</summary>
    public List<SegmentEntry> Segments { get; set; } = new();

    /// <summary>
    /// Request headers (cookies, referer, …) to send when fetching an AES-128 key. The key usually lives on
    /// the same protected origin as the playlist, but it is fetched at ASSEMBLY time — long after the
    /// segments — so without this it was the one request that went out anonymous, and it failed at the very
    /// end of an otherwise complete download. Null (every recipe written before this) ⇒ a bare request,
    /// exactly as before.
    /// </summary>
    public Dictionary<string, string>? KeyHeaders { get; set; }

    /// <summary>The stream groups, treating a recipe without <see cref="Streams"/> as a single group.</summary>
    public IReadOnlyList<StreamGroup> StreamsOrSingle() =>
        Streams is { Count: > 0 }
            ? Streams
            : new List<StreamGroup> { new() { HasInitSegment = HasInitSegment, SegmentCount = Segments.Count } };
}

/// <summary>One elementary stream inside a concat recipe: its optional init segment plus how many of the
/// recipe's media segments belong to it.</summary>
public sealed class StreamGroup
{
    /// <summary>The group's first downloaded file is an init segment, prepended verbatim and not counted in
    /// <see cref="SegmentCount"/>.</summary>
    public bool HasInitSegment { get; set; }

    /// <summary>How many media segments this group owns.</summary>
    public int SegmentCount { get; set; }

    /// <summary>Total downloaded files this group consumes.</summary>
    public int FileCount => SegmentCount + (HasInitSegment ? 1 : 0);
}

/// <summary>Per-segment decrypt info. When <see cref="Encrypted"/> is false the segment is concatenated as-is.</summary>
public sealed class SegmentEntry
{
    public bool Encrypted { get; set; }
    public string? KeyUri { get; set; }
    /// <summary>16-byte IV as 32 hex chars (no <c>0x</c> prefix).</summary>
    public string? IvHex { get; set; }
}
