using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Downloader.Desktop.Plugins.SiteMedia;

/// <summary>Combining a downloaded video-only and audio-only file into one playable file. Behind an
/// interface so the post-processor is unit-tested without ffmpeg.</summary>
public interface IMediaMuxer
{
    Task MuxAsync(string videoFile, string audioFile, string outputPath, CancellationToken cancellationToken);
}

/// <summary>
/// The real <see cref="IMediaMuxer"/>: locates ffmpeg (cached in this plugin's data dir → system PATH →
/// downloaded on first use) and stream-copies the two inputs into one MP4. Nothing is bundled in the
/// installer; the streaming-media plugin provisions its own copy the same way, because the two plugins are
/// installed independently and neither may depend on the other being present.
/// </summary>
public sealed class FfmpegMuxer : IMediaMuxer
{
    // Static build sources (no bundling). Kept here so the URL surface is obvious.
    private const string WinUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    private const string LinuxUrl = "https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz";
    private const string MacUrl = "https://evermeet.cx/ffmpeg/getrelease/zip";

    private readonly string _dataDir;
    private readonly HttpClient _http;
    private readonly ILogger _log;
    private string? _resolved;

    public FfmpegMuxer(string dataDirectory, HttpClient? http = null, ILogger? logger = null)
    {
        _dataDir = dataDirectory;
        // Infinite timeout: the default 100 s covers the whole body read and truncates a large static
        // build on slow links. Cancellation comes from the caller's token.
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _log = logger ?? NullLogger.Instance;
    }

    public async Task MuxAsync(string videoFile, string audioFile, string outputPath, CancellationToken cancellationToken)
    {
        var exe = await EnsureFfmpegAsync(cancellationToken).ConfigureAwait(false);

        var args = $"-y -i \"{videoFile}\" -i \"{audioFile}\" -c copy -movflags +faststart \"{outputPath}\"";
        _log.LogInformation("Running ffmpeg (mux): {Exe} {Args}", exe, args);

        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false, // absolute path, our own arguments, no shell — issue #4
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffmpeg.");
        var stderr = await proc.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg exited with code {proc.ExitCode}: {Tail(stderr)}");
    }

    /// <summary>Resolve ffmpeg: cached binary → system PATH → download on first use.</summary>
    public async Task<string> EnsureFfmpegAsync(CancellationToken cancellationToken)
    {
        if (_resolved is not null) return _resolved;

        var cached = TargetExePath(_dataDir);
        if (ToolFile.IsUsable(cached)) return _resolved = cached;
        if (File.Exists(cached))
        {
            _log.LogWarning("The cached ffmpeg at {Path} is incomplete — installing it again", cached);
            ToolFile.DeleteIfPresent(cached);
        }

        var onPath = ToolFile.FindOnPath(ExeName);
        if (onPath is not null) return _resolved = onPath;

        _log.LogInformation("ffmpeg not found; downloading a static build into {Dir}", _dataDir);
        var archive = ArchivePath(_dataDir);
        try
        {
            await ToolFile.DownloadToAsync(_http, ResolveDownloadUrl(), archive, cancellationToken)
                .ConfigureAwait(false);
            await FinishInstallAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ToolFile.DeleteIfPresent(archive); // never leave a partial archive to poison later attempts
            throw;
        }
        return _resolved = cached;
    }

    /// <summary>Declares ffmpeg as a <see cref="PluginBinaryDependency"/> so the host fetches it (resumable,
    /// with progress) at plugin-install time instead of silently on first real use.</summary>
    public PluginBinaryDependency GetDependency() => new()
    {
        Id = "ffmpeg",
        DisplayName = "FFmpeg",
        DownloadUrl = new Uri(ResolveDownloadUrl()),
        DownloadDestination = ArchivePath(_dataDir),
        IsAvailable = () => ToolFile.IsUsable(TargetExePath(_dataDir)) || ToolFile.FindOnPath(ExeName) is not null,
        FinishInstallAsync = FinishInstallAsync,
    };

    private static string ResolveDownloadUrl() =>
        OperatingSystem.IsWindows() ? WinUrl
        : OperatingSystem.IsMacOS() ? MacUrl
        : OperatingSystem.IsLinux() ? LinuxUrl
        : throw new PlatformNotSupportedException("No ffmpeg static build configured for this OS.");

    private static string TargetExePath(string dataDir) => Path.Combine(dataDir, "ffmpeg-bin", ExeName);

    private static string ArchivePath(string dataDir)
    {
        var binDir = Path.Combine(dataDir, "ffmpeg-bin");
        var ext = Path.GetExtension(new Uri(ResolveDownloadUrl()).AbsolutePath);
        return Path.Combine(binDir, "ffmpeg-download" + (string.IsNullOrEmpty(ext)
            ? (OperatingSystem.IsLinux() ? ".tar.xz" : ".zip")
            : ext));
    }

    private Task FinishInstallAsync(CancellationToken ct)
    {
        var binDir = Path.Combine(_dataDir, "ffmpeg-bin");
        Directory.CreateDirectory(binDir);
        var archive = ArchivePath(_dataDir);
        var targetExe = TargetExePath(_dataDir);

        try
        {
            Extract(archive, binDir);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A truncated/corrupt archive must not survive — delete it so the next attempt re-downloads
            // instead of failing on the same bad file forever.
            ToolFile.DeleteIfPresent(archive);
            throw new InvalidOperationException(
                "The downloaded FFmpeg archive was corrupt; it will be re-downloaded on the next attempt.", ex);
        }
        ToolFile.DeleteIfPresent(archive);

        var found = Directory.EnumerateFiles(binDir, ExeName, SearchOption.AllDirectories).FirstOrDefault()
                    ?? throw new InvalidOperationException("ffmpeg binary not found in the downloaded archive.");

        if (found != targetExe)
            File.Copy(found, targetExe, overwrite: true);
        ToolFile.MakeExecutable(targetExe);

        return Task.CompletedTask;
    }

    private static void Extract(string archive, string destDir)
    {
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archive, destDir, overwriteFiles: true);
            return;
        }
        // .tar.xz / .tar.gz — the system tar, by absolute path where we know it (never via a shell).
        var tar = ToolFile.FindOnPath("tar") ?? "/usr/bin/tar";
        var psi = new ProcessStartInfo(tar, $"-xf \"{archive}\" -C \"{destDir}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start tar.");
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"tar failed to extract {archive} (exit {p.ExitCode}).");
    }

    private static string ExeName => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";

    private static string Tail(string s, int max = 600) => s.Length <= max ? s : s[^max..];
}
