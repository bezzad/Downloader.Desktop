using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Downloader.Desktop.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Downloader.Desktop.Plugins.Hls;

/// <summary>
/// Real <see cref="IFfmpeg"/>: locates ffmpeg (cached in the plugin data dir → system PATH → downloaded on
/// first use), then runs a stream-copy remux. A static ffmpeg build is downloaded per-OS into the data
/// directory the first time it is needed — nothing is bundled in the installer.
/// </summary>
public sealed class FfmpegBinary : IFfmpeg
{
    // Static build sources (no bundling). These can be updated as needed; kept here so the URL surface is
    // obvious. Linux/macOS archives are extracted with the system `tar`; Windows with System.IO.Compression.
    private const string WinUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    private const string LinuxUrl = "https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz";
    private const string MacUrl = "https://evermeet.cx/ffmpeg/getrelease/zip";

    private readonly string _dataDir;
    private readonly HttpClient _http;
    private readonly ILogger _log;
    private string? _resolved;

    public FfmpegBinary(string dataDirectory, HttpClient? http = null, ILogger? logger = null)
    {
        _dataDir = dataDirectory;
        // Infinite timeout: the default 100 s covers the whole body read and truncates a large static
        // build on slow links. Cancellation comes from the caller's token.
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _log = logger ?? NullLogger.Instance;
    }

    public async Task RemuxAsync(string inputFile, string outputPath, CancellationToken cancellationToken)
    {
        var exe = await EnsureFfmpegAsync(cancellationToken).ConfigureAwait(false);

        // HLS .ts → MP4: copy streams, fix AAC ADTS→ASC, generate timestamps, faststart for playback.
        var args = $"-y -fflags +genpts -i \"{inputFile}\" -c copy -bsf:a aac_adtstoasc -movflags +faststart \"{outputPath}\"";
        _log.LogInformation("Running ffmpeg: {Exe} {Args}", exe, args);

        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi)
                         ?? throw new InvalidOperationException("Failed to start ffmpeg.");
        var stderr = await proc.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg exited with code {proc.ExitCode}: {Tail(stderr)}");
    }

    /// <summary>
    /// Combine a video-only and an audio-only file into one MP4 with stream copy. The maps are EXPLICIT:
    /// relying on ffmpeg's default stream selection picks one stream per type across BOTH inputs, so a
    /// video file that happens to carry a silent/secondary audio track can win and the real audio is
    /// dropped — a file that plays with no sound. AAC arriving in MPEG-TS is ADTS-framed and needs
    /// <c>aac_adtstoasc</c> to be legal inside MP4; fMP4/MP4 audio already carries its ASC and must not be
    /// filtered.
    /// </summary>
    internal static string BuildMuxArgs(string videoFile, string audioFile, string outputPath)
    {
        var bsf = IsAdtsContainer(audioFile) ? "-bsf:a aac_adtstoasc " : string.Empty;
        return $"-y -i \"{videoFile}\" -i \"{audioFile}\" -map 0:v:0 -map 1:a:0 -c copy {bsf}-movflags +faststart \"{outputPath}\"";
    }

    private static bool IsAdtsContainer(string file)
    {
        var ext = Path.GetExtension(file);
        return ext.Equals(".ts", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".aac", StringComparison.OrdinalIgnoreCase);
    }

    public async Task MuxAsync(string videoFile, string audioFile, string outputPath, CancellationToken cancellationToken)
    {
        var exe = await EnsureFfmpegAsync(cancellationToken).ConfigureAwait(false);

        var args = BuildMuxArgs(videoFile, audioFile, outputPath);
        _log.LogInformation("Running ffmpeg (mux): {Exe} {Args}", exe, args);

        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi)
                         ?? throw new InvalidOperationException("Failed to start ffmpeg.");
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
        if (BinaryFile.IsUsable(cached)) return _resolved = cached;
        if (File.Exists(cached))
        {
            _log.LogWarning("The cached ffmpeg at {Path} is incomplete — installing it again", cached);
            BinaryFile.DeleteIfPresent(cached);
        }

        var onPath = FindOnPath();
        if (onPath is not null) return _resolved = onPath;

        _log.LogInformation("ffmpeg not found; downloading a static build into {Dir}", _dataDir);
        var archive = ArchivePath(_dataDir);
        try
        {
            await BinaryFile.DownloadToAsync(_http, ResolveDownloadUrl(), archive, cancellationToken)
                .ConfigureAwait(false);
            await FinishInstallAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Never leave a partial archive behind — it would poison every later attempt.
            try { File.Delete(archive); } catch (IOException) { }
            throw;
        }
        return _resolved = cached;
    }

    /// <summary>
    /// Declares ffmpeg as a <see cref="PluginBinaryDependency"/> so the host can fetch it (resumable, with
    /// progress) at plugin-install time instead of silently on first real use.
    /// </summary>
    public PluginBinaryDependency GetDependency() => new()
    {
        Id = "ffmpeg",
        DisplayName = "FFmpeg",
        DownloadUrl = new Uri(ResolveDownloadUrl()),
        DownloadDestination = ArchivePath(_dataDir),
        IsAvailable = () => BinaryFile.IsUsable(TargetExePath(_dataDir)) || FindOnPath() is not null,
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

    /// <summary>Extract/place an already-downloaded archive (at <see cref="ArchivePath"/>) into the plugin's
    /// cached ffmpeg location. Called both by the self-contained lazy download and by the host after it
    /// downloads the archive itself (see <see cref="GetDependency"/>).</summary>
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
            // A truncated/corrupt archive (interrupted download) must not survive — delete it so the
            // next attempt re-downloads instead of failing on the same bad file forever.
            try { File.Delete(archive); } catch (IOException) { }
            throw new InvalidOperationException("The downloaded FFmpeg archive was corrupt; it will be re-downloaded on the next attempt.", ex);
        }
        try { File.Delete(archive); } catch (IOException) { }

        var found = Directory.EnumerateFiles(binDir, ExeName, SearchOption.AllDirectories).FirstOrDefault()
                    ?? throw new InvalidOperationException("ffmpeg binary not found in the downloaded archive.");

        if (found != targetExe)
            File.Copy(found, targetExe, overwrite: true);
        BinaryFile.MakeExecutable(targetExe);

        return Task.CompletedTask;
    }

    private static void Extract(string archive, string destDir)
    {
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archive, destDir, overwriteFiles: true);
            return;
        }
        // .tar.xz / .tar.gz — use the system tar (present on Linux/macOS).
        var psi = new ProcessStartInfo("tar", $"-xf \"{archive}\" -C \"{destDir}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start tar.");
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"tar failed to extract {archive} (exit {p.ExitCode}).");
    }


    private static string? FindOnPath()
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar)) return null;
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var candidate = Path.Combine(dir, ExeName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string ExeName => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";

    private static string Tail(string s, int max = 600) =>
        s.Length <= max ? s : s[^max..];
}
