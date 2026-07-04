using System;
using System.Net;

namespace Downloader.Desktop.Models;

/// <summary>
/// User-facing, JSON-persistable mirror of the engine's <see cref="DownloadConfiguration"/> options
/// (which itself can't be serialized because it holds delegates and non-serializable request objects).
/// Grouped into Basic / Advanced / Request for the Settings UI; <see cref="ToConfiguration"/> maps to the engine.
/// </summary>
public class DownloadSettings
{
    // ---- Basic ----
    public string DefaultSavePath { get; set; }

    /// <summary>
    /// When true (default), the folder chosen for a new download becomes the default save path for
    /// the next one. When false, adding a download with a custom folder leaves the default save path
    /// unchanged.
    /// </summary>
    public bool RememberLastSavePath { get; set; } = true;

    /// <summary>Number of parts (connections) a file is split into.</summary>
    public int ChunkCount { get; set; } = 8;

    public bool ParallelDownload { get; set; } = true;

    /// <summary>How many chunks download at once. 0 = same as <see cref="ChunkCount"/>.</summary>
    public int ParallelCount { get; set; } = 0;

    /// <summary>Global speed cap in bytes/second. 0 = unlimited.</summary>
    public long MaximumBytesPerSecond { get; set; } = 0;

    /// <summary>App-level cap on how many downloads run at once within a queue.</summary>
    public int MaxConcurrentDownloads { get; set; } = 3;

    /// <summary>Write a diagnostic log file (off by default).</summary>
    public bool EnableLogging { get; set; } = false;

    /// <summary>Master switch for desktop notifications (on by default). The per-event toggles below
    /// only fire when this is on.</summary>
    public bool EnableNotifications { get; set; } = true;

    /// <summary>Notify when a single download finishes (on by default).</summary>
    public bool NotifyOnComplete { get; set; } = true;

    /// <summary>Notify when a download fails (on by default).</summary>
    public bool NotifyOnFailed { get; set; } = true;

    /// <summary>Notify once when every active download has finished (on by default).</summary>
    public bool NotifyOnAllComplete { get; set; } = true;

    /// <summary>Notify (with a 30 s cancel option) when the system is about to shut down because all
    /// downloads completed (on by default). Only relevant when <see cref="ShutdownOnCompletion"/> is on.</summary>
    public bool NotifyOnShutdown { get; set; } = true;

    /// <summary>Power the computer off once every active download has finished (off by default). A 30 s
    /// cancelable notice is shown first so a user at the keyboard can stop it.</summary>
    public bool ShutdownOnCompletion { get; set; } = false;

    /// <summary>Listen on a local loopback port so the browser extension and local scripts/CLI can
    /// add and control downloads (on by default since the local API shipped — loopback only; older
    /// configs are migrated once via <see cref="Config.SchemaVersion"/>).</summary>
    public bool EnableBrowserIntegration { get; set; } = true;

    /// <summary>Last local-API port the listener actually bound to (from the declared 15151–15155 range).
    /// 0 = not yet determined; the app prefers this on the next start before falling back further, and the
    /// CLI reads it to reach the running instance. Not user-editable (the extension can only reach the
    /// pre-declared range).</summary>
    public int LocalApiPort { get; set; } = 0;

    /// <summary>UI language code (en, fa, es, fr, ar, eo). Default English.</summary>
    public string Language { get; set; } = "en";

    /// <summary>Accent color key (Teal/Blue/Purple/Green/Amber). Applied on top of Light/Dark by ThemeService.</summary>
    public string AccentColor { get; set; } = "Teal";

    /// <summary>Keep the app running in the system tray when the main window is closed (on by default).</summary>
    public bool EnableSystemTray { get; set; } = true;

    /// <summary>Launch the app (hidden to tray) when the OS starts. Requires the tray to be enabled.</summary>
    public bool RunAtStartup { get; set; } = false;

    /// <summary>Check GitHub for a newer release and offer to update (on by default).</summary>
    public bool AutoUpdate { get; set; } = true;

    // ---- Advanced ----
    public int BufferBlockSize { get; set; } = 8192;
    public int MaxTryAgainOnFailure { get; set; } = 5;

    /// <summary>
    /// Per-block read deadline in ms (the timeout for a single <see cref="BufferBlockSize"/>-sized
    /// read), NOT a connection timeout. Kept generous (5 s) because throttled/bursty servers (e.g.
    /// video CDNs) routinely pause more than a second between bursts on a healthy connection; a
    /// too-small value turns those normal pauses into spurious "connection timed out" failures.
    /// </summary>
    public int BlockTimeout { get; set; } = 5000;

    /// <summary>
    /// Overall HttpClient timeout in ms — this is the timeout for the WHOLE chunk request incl.
    /// reading its body, so it must be generous or long chunks fail with "Operation Cancelled".
    /// Per-block stalls are handled separately by <see cref="BlockTimeout"/>. Default 100 s (engine default).
    /// </summary>
    public int HttpClientTimeout { get; set; } = 100_000;
    public long MinimumSizeOfChunking { get; set; } = 512;
    public long MinimumChunkSize { get; set; } = 0;

    /// <summary>
    /// Max RAM used for buffering before flushing to disk. 0 = unlimited. Default 2 GB — an
    /// unbounded buffer can stall throughput once memory fills, so we cap it by default.
    /// </summary>
    public long MaximumMemoryBufferBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    public bool CheckDiskSizeBeforeDownload { get; set; } = true;
    public bool EnableAutoResumeDownload { get; set; } = true;
    public bool ClearPackageOnCompletionWithFailure { get; set; } = false;

    /// <summary>What to do when the target file already exists.</summary>
    public FileExistPolicy FileExistPolicy { get; set; } = FileExistPolicy.IgnoreDownload;

    public string DownloadFileExtension { get; set; } = ".download";

    // ---- Request (common HTTP options) ----
    public string UserAgent { get; set; }
    public string Referer { get; set; }
    public string Accept { get; set; }
    public bool AllowAutoRedirect { get; set; } = true;
    public int MaximumAutomaticRedirections { get; set; } = 50;
    public int ConnectTimeout { get; set; } = 30_000;
    public bool KeepAlive { get; set; } = false;

    /// <summary>Optional proxy, e.g. "http://host:port". Empty = no proxy.</summary>
    public string ProxyAddress { get; set; }

    public static DownloadSettings New() => new()
    {
        DefaultSavePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { } home
            ? System.IO.Path.Combine(home, "Downloads")
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
    };

    /// <summary>Builds an engine <see cref="DownloadConfiguration"/> from these settings.</summary>
    public DownloadConfiguration ToConfiguration()
    {
        var cfg = new DownloadConfiguration
        {
            ChunkCount = Math.Max(1, ChunkCount),
            ParallelDownload = ParallelDownload,
            ParallelCount = Math.Max(0, ParallelCount),
            MaximumBytesPerSecond = MaximumBytesPerSecond <= 0 ? 0 : MaximumBytesPerSecond,
            BufferBlockSize = Math.Clamp(BufferBlockSize, 1, 1024 * 1024),
            MaxTryAgainOnFailure = Math.Max(0, MaxTryAgainOnFailure),
            BlockTimeout = Math.Max(100, BlockTimeout),
            HttpClientTimeout = Math.Max(1000, HttpClientTimeout),
            MinimumSizeOfChunking = Math.Max(0, MinimumSizeOfChunking),
            MinimumChunkSize = Math.Max(0, MinimumChunkSize),
            MaximumMemoryBufferBytes = Math.Max(0, MaximumMemoryBufferBytes),
            CheckDiskSizeBeforeDownload = CheckDiskSizeBeforeDownload,
            EnableAutoResumeDownload = EnableAutoResumeDownload,
            ClearPackageOnCompletionWithFailure = ClearPackageOnCompletionWithFailure,
            FileExistPolicy = FileExistPolicy
        };

        if (!string.IsNullOrWhiteSpace(DownloadFileExtension))
            cfg.DownloadFileExtension = DownloadFileExtension;

        var req = cfg.RequestConfiguration;
        if (!string.IsNullOrWhiteSpace(UserAgent)) req.UserAgent = UserAgent;
        if (!string.IsNullOrWhiteSpace(Referer)) req.Referer = Referer;
        if (!string.IsNullOrWhiteSpace(Accept)) req.Accept = Accept;
        req.AllowAutoRedirect = AllowAutoRedirect;
        req.MaximumAutomaticRedirections = Math.Max(1, MaximumAutomaticRedirections);
        req.ConnectTimeout = Math.Max(1000, ConnectTimeout);
        req.KeepAlive = KeepAlive;
        if (!string.IsNullOrWhiteSpace(ProxyAddress))
        {
            try { req.Proxy = new WebProxy(ProxyAddress); }
            catch { /* ignore malformed proxy address */ }
        }

        return cfg;
    }
}
