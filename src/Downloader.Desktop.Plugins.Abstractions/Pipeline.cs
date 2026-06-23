namespace Downloader.Desktop.Plugins;

// ── Phase 1: RESOLVE ───────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Turns a pasted link the core engine can't download directly (a web page, a redirect, a github repo, an
/// HLS playlist, …) into a concrete <see cref="DownloadPlan"/> of real file URLs. The resolver does NOT
/// download — it only resolves the link; the engine downloads the resulting parts.
/// </summary>
public interface ILinkResolver
{
    /// <summary>Fast, cheap check: does this resolver claim the link? (e.g. host == github.com)</summary>
    bool CanResolve(string url);

    /// <summary>Resolve the input into real downloadable parts + a post-process recipe.</summary>
    Task<DownloadPlan> ResolveAsync(string url, CancellationToken cancellationToken);
}

/// <summary>The result of resolving: the real parts to download + how to combine them afterwards.</summary>
public sealed class DownloadPlan
{
    public string? SuggestedFileName { get; init; }
    public IReadOnlyList<DownloadPart> Parts { get; init; } = Array.Empty<DownloadPart>();
    public PostProcess PostProcess { get; init; } = PostProcess.None;
}

/// <summary>One real, downloadable stream/segment. The core engine downloads each part into a temp file.</summary>
public sealed class DownloadPart
{
    public string Url { get; init; } = string.Empty;
    public PartKind Kind { get; init; } = PartKind.Combined;
    /// <summary>Per-request headers (cookies/referer/range) the engine must send for this part.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
    /// <summary>Known size in bytes, if the resolver provided one (enables byte-weighted progress).</summary>
    public long? ExpectedSize { get; init; }
}

public enum PartKind { Combined, Video, Audio, Segment, Subtitle }

// ── Phase 3: POST-PROCESS ──────────────────────────────────────────────────────────────────────────

/// <summary>How the downloaded part files are combined into the final output.</summary>
public sealed class PostProcess
{
    public static readonly PostProcess None = new() { Kind = PostProcessKind.None };

    public PostProcessKind Kind { get; init; } = PostProcessKind.None;
    /// <summary>Free-form recipe hint for the processor (e.g. ffmpeg argument template).</summary>
    public string? Recipe { get; init; }
}

public enum PostProcessKind { None, Mux, Concat, Decrypt }

/// <summary>Combines downloaded part files into the final file (ffmpeg mux/concat, decrypt, …).</summary>
public interface IPostProcessor
{
    bool CanProcess(PostProcess plan);

    Task<string> ProcessAsync(
        IReadOnlyList<string> inputFiles,
        PostProcess plan,
        string outputPath,
        IProgress<double> progress,
        CancellationToken cancellationToken);
}

// ── Phase 2: TRANSFER (alternative download mechanism, e.g. torrent) ─────────────────────────────────

/// <summary>An alternative way to fetch bytes when the core HTTP engine can't (torrent, etc.).</summary>
public interface ITransferProvider
{
    /// <summary>e.g. true for "magnet:" or ".torrent" inputs.</summary>
    bool CanHandle(string url);

    /// <summary>Create a transfer that owns the whole download for this input.</summary>
    ITransfer Create(string url, string targetFolder);
}

/// <summary>A self-managed transfer (it aggregates its own progress; the Job sees it as one unit).</summary>
public interface ITransfer
{
    event EventHandler<TransferProgress>? ProgressChanged;

    /// <summary>Start (or resume) the transfer; returns the path of the produced file when complete.</summary>
    Task<string> StartAsync(CancellationToken cancellationToken);

    void Pause();
    void Resume();
}

public sealed class TransferProgress
{
    public double Percentage { get; init; }
    public long BytesReceived { get; init; }
    public long TotalBytes { get; init; }
    public double BytesPerSecond { get; init; }
}
