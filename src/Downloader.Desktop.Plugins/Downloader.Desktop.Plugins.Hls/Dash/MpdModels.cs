namespace Downloader.Desktop.Plugins.Hls.Dash;

/// <summary>Which elementary stream a representation carries.</summary>
public enum DashStreamKind { Video, Audio, Other }

/// <summary>
/// One fully expanded representation: every segment URL is already absolute, in playback order, so the
/// resolver never has to know which addressing mode the manifest used.
/// </summary>
/// <param name="IsSingleFile">True when the representation is one complete file (a <c>SegmentBase</c> or
/// bare <c>BaseURL</c> on-demand profile) rather than a segment list — the host downloads it as a normal
/// multi-chunk file instead of many small segment parts.</param>
public sealed record DashRepresentation(
    string Id,
    DashStreamKind Kind,
    long Bandwidth,
    int Width,
    int Height,
    string? Codecs,
    string? Language,
    string? InitSegmentUri,
    IReadOnlyList<string> SegmentUris,
    bool IsSingleFile);

/// <summary>A parsed static MPD: its declared duration and the representations of its first period.</summary>
public sealed record DashManifest(double DurationSeconds, IReadOnlyList<DashRepresentation> Representations)
{
    public IReadOnlyList<DashRepresentation> Video =>
        Representations.Where(r => r.Kind == DashStreamKind.Video).ToList();

    public IReadOnlyList<DashRepresentation> Audio =>
        Representations.Where(r => r.Kind == DashStreamKind.Audio).ToList();

    /// <summary>Highest-bandwidth video representation, or null when the manifest carries no video.</summary>
    public DashRepresentation? BestVideo() => Video.OrderByDescending(r => r.Bandwidth).FirstOrDefault();

    /// <summary>Highest-bandwidth audio representation, or null when the manifest carries no audio.</summary>
    public DashRepresentation? BestAudio() => Audio.OrderByDescending(r => r.Bandwidth).FirstOrDefault();
}
