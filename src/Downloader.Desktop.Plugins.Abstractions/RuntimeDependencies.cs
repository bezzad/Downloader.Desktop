namespace Downloader.Desktop.Plugins;

/// <summary>
/// A runtime binary a plugin needs but doesn't bundle in its package (e.g. ffmpeg, yt-dlp). The HOST
/// downloads/resumes <see cref="DownloadUrl"/> into <see cref="DownloadDestination"/> using its own
/// resumable download engine, then calls <see cref="FinishInstallAsync"/> so the plugin can finish placing
/// the binary (extract an archive, chmod +x, copy into its final location). <see cref="IsAvailable"/> lets
/// the host skip a dependency that's already cached locally or found on PATH, without downloading anything.
/// </summary>
public sealed class PluginBinaryDependency
{
    /// <summary>Stable id for this dependency within the plugin, e.g. "ffmpeg".</summary>
    public required string Id { get; init; }

    /// <summary>User-facing name shown in install progress, e.g. "FFmpeg".</summary>
    public required string DisplayName { get; init; }

    /// <summary>Where to download the raw file from.</summary>
    public required Uri DownloadUrl { get; init; }

    /// <summary>The fixed local path the host downloads the raw file to. Kept stable across attempts so an
    /// interrupted download (cancelled, app closed, network drop) resumes from where it left off next time.</summary>
    public required string DownloadDestination { get; init; }

    /// <summary>True when this dependency is already available (cached from a previous install, or found on
    /// PATH) — the host skips downloading it entirely.</summary>
    public required Func<bool> IsAvailable { get; init; }

    /// <summary>Finish installing from the file the host downloaded to <see cref="DownloadDestination"/>
    /// (extract an archive, chmod +x, move into its final resolved location). Called once after the
    /// download completes successfully.</summary>
    public required Func<CancellationToken, Task> FinishInstallAsync { get; init; }
}

/// <summary>Progress of fetching one <see cref="PluginBinaryDependency"/> out of the full set being ensured.</summary>
/// <param name="DependencyName">The dependency's <see cref="PluginBinaryDependency.DisplayName"/>.</param>
/// <param name="PercentComplete">0-100 progress of the current dependency's download.</param>
/// <param name="Index">1-based position of the current dependency among those being fetched.</param>
/// <param name="Total">How many dependencies are being fetched this run.</param>
public sealed record PluginDependencyProgress(string DependencyName, double PercentComplete, int Index, int Total);

/// <summary>
/// Implemented by a plugin that needs external runtime binaries beyond its own package (e.g. the HLS
/// plugin's ffmpeg/yt-dlp). The host calls this before finalizing an "Add" from the plugin catalog, downloads
/// (resumable) whatever isn't already <see cref="PluginBinaryDependency.IsAvailable"/>, then finishes each
/// one — the plugin only appears installed once every dependency is ready.
/// </summary>
public interface IHasRuntimeDependencies
{
    /// <summary>Declare the external binaries this plugin needs. <paramref name="dataDirectory"/> is this
    /// plugin's private writable data folder (same as <see cref="IPluginContext.DataDirectory"/>).</summary>
    IReadOnlyList<PluginBinaryDependency> GetRequiredDependencies(string dataDirectory);
}
