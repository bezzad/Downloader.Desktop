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

    /// <summary>One entry per media segment, in playlist order (maps 1:1 to the downloaded segment files).</summary>
    public List<SegmentEntry> Segments { get; set; } = new();
}

/// <summary>Per-segment decrypt info. When <see cref="Encrypted"/> is false the segment is concatenated as-is.</summary>
public sealed class SegmentEntry
{
    public bool Encrypted { get; set; }
    public string? KeyUri { get; set; }
    /// <summary>16-byte IV as 32 hex chars (no <c>0x</c> prefix).</summary>
    public string? IvHex { get; set; }
}
