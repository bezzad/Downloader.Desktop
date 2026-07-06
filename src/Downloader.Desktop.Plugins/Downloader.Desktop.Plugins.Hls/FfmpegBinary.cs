using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
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
        _http = http ?? new HttpClient();
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

    public async Task MuxAsync(string videoFile, string audioFile, string outputPath, CancellationToken cancellationToken)
    {
        var exe = await EnsureFfmpegAsync(cancellationToken).ConfigureAwait(false);

        // Combine separate video-only + audio-only streams into one MP4: copy both, faststart for playback.
        var args = $"-y -i \"{videoFile}\" -i \"{audioFile}\" -c copy -movflags +faststart \"{outputPath}\"";
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

        var cached = Path.Combine(_dataDir, "ffmpeg-bin", ExeName);
        if (File.Exists(cached)) return _resolved = cached;

        var onPath = FindOnPath();
        if (onPath is not null) return _resolved = onPath;

        _log.LogInformation("ffmpeg not found; downloading a static build into {Dir}", _dataDir);
        return _resolved = await DownloadAsync(cached, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> DownloadAsync(string targetExe, CancellationToken ct)
    {
        var url = OperatingSystem.IsWindows() ? WinUrl
                : OperatingSystem.IsMacOS() ? MacUrl
                : OperatingSystem.IsLinux() ? LinuxUrl
                : throw new PlatformNotSupportedException("No ffmpeg static build configured for this OS.");

        var binDir = Path.Combine(_dataDir, "ffmpeg-bin");
        Directory.CreateDirectory(binDir);
        var archive = Path.Combine(binDir, "ffmpeg-download" + Path.GetExtension(new Uri(url).AbsolutePath));
        if (string.IsNullOrEmpty(Path.GetExtension(archive)))
            archive = Path.Combine(binDir, OperatingSystem.IsLinux() ? "ffmpeg.tar.xz" : "ffmpeg.zip");

        await using (var fs = File.Create(archive))
        await using (var stream = await _http.GetStreamAsync(url, ct).ConfigureAwait(false))
            await stream.CopyToAsync(fs, ct).ConfigureAwait(false);

        Extract(archive, binDir);
        try { File.Delete(archive); } catch (IOException) { }

        var found = Directory.EnumerateFiles(binDir, ExeName, SearchOption.AllDirectories).FirstOrDefault()
                    ?? throw new InvalidOperationException("ffmpeg binary not found in the downloaded archive.");

        if (found != targetExe)
        {
            File.Copy(found, targetExe, overwrite: true);
        }
        if (!OperatingSystem.IsWindows())
            MakeExecutable(targetExe);

        return targetExe;
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

    private static void MakeExecutable(string path)
    {
        var psi = new ProcessStartInfo("chmod", $"+x \"{path}\"") { UseShellExecute = false, CreateNoWindow = true };
        try { Process.Start(psi)?.WaitForExit(); } catch { /* best effort */ }
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
