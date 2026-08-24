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

    /// <summary>A fallback resolver claims broad/generic links (e.g. "any web page"). The host consults
    /// fallback resolvers only when no regular resolver claims the link, so a generic plugin can never
    /// shadow a specific one (GitHub, video sites, …). Default-implemented to false, so existing and
    /// external plugins keep working unchanged.</summary>
    bool IsFallback => false;

    /// <summary>Resolve the input into real downloadable parts + a post-process recipe.</summary>
    Task<DownloadPlan> ResolveAsync(string url, CancellationToken cancellationToken);

    /// <summary>Resolve with extra per-request options (e.g. a browser-supplied cookie file for sites that
    /// need a signed-in session). Default-implemented to ignore the options, so existing resolvers and
    /// external plugins keep working unchanged; only resolvers that need them (e.g. the HLS/yt-dlp one)
    /// override this.</summary>
    Task<DownloadPlan> ResolveAsync(string url, ResolveOptions options, CancellationToken cancellationToken)
        => ResolveAsync(url, cancellationToken);

    /// <summary>List the selectable variants behind this link (video qualities, model tags, release
    /// assets, …) so the host can let the user pick before downloading. Null/empty = this link offers no
    /// choices and the host resolves it directly. Default-implemented so existing resolvers and external
    /// plugins keep working unchanged; a chosen variant comes back via
    /// <see cref="ResolveOptions.VariantId"/>.</summary>
    Task<IReadOnlyList<LinkVariant>?> GetVariantsAsync(string url, ResolveOptions? options, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<LinkVariant>?>(null);
}

/// <summary>One selectable variant behind a link (a quality, a model tag, an asset…).</summary>
public sealed class LinkVariant
{
    /// <summary>Resolver-defined stable key (e.g. "1080", "audio", "gemma3:12b") — round-trips through
    /// <see cref="ResolveOptions.VariantId"/> and persists on the download for retries.</summary>
    public required string Id { get; init; }

    /// <summary>User-facing label (e.g. "1080p (≈460 MB)").</summary>
    public required string Label { get; init; }

    public string? Description { get; init; }

    /// <summary>Approximate size in bytes when known (display only).</summary>
    public long? ExpectedSize { get; init; }

    /// <summary>Pre-checked in the picker; what a variant-less resolve would pick.</summary>
    public bool IsDefault { get; init; }

    /// <summary>When the variant IS a distinct, independently-resolvable link (an Ollama tag, a release
    /// asset…), the input the host should use INSTEAD of the pasted one — the created download carries
    /// this as its URL and no <see cref="ResolveOptions.VariantId"/>. Null for facet variants (e.g. a
    /// video quality) where the original link plus the variant id drive the resolve.</summary>
    public string? SubstituteUrl { get; init; }
}

/// <summary>Optional per-request inputs for a resolve call. All members are optional/nullable.</summary>
public sealed class ResolveOptions
{
    /// <summary>Path to a temporary Netscape-format cookie file (from a live browser session handed over by
    /// the extension) to try before any on-disk browser cookie store. Null = none supplied.</summary>
    public string? CookieFilePath { get; init; }

    /// <summary>The <see cref="LinkVariant.Id"/> the user chose, or null for the resolver's default pick.</summary>
    public string? VariantId { get; init; }

    /// <summary>Request headers this download must be fetched with (supplied per download by the host, e.g.
    /// from the browser extension). A resolver SHOULD send them on its own manifest/API requests and stamp
    /// them onto every <see cref="DownloadPart"/> it produces, so the segments are fetched with the same
    /// context. Any referer the host was given appears here as a normal <c>Referer</c> entry. Null = none.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
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
