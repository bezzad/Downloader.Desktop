using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Downloader.Desktop.Plugins.Hls;

/// <summary>
/// Real <see cref="IYtDlp"/>: locates yt-dlp (cached in the plugin data dir → system PATH → downloaded on
/// first use), then runs <c>yt-dlp -J</c> to dump the media metadata as JSON. yt-dlp ships as a single
/// self-contained per-OS binary (no archive, no Python), so provisioning is a plain download + chmod —
/// nothing is bundled in the installer, mirroring <see cref="FfmpegBinary"/>.
/// </summary>
public sealed class YtDlpBinary : IYtDlp
{
    // Latest self-contained builds from the yt-dlp GitHub releases. Kept here so the URL surface is obvious;
    // yt-dlp self-updates as sites change, so "latest" is intentional.
    private const string ReleaseBase = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/";

    private readonly string _dataDir;
    private readonly HttpClient _http;
    private readonly ILogger _log;
    private string? _resolved;
    private string? _denoResolved; // "" = provisioning failed, don't retry

    public YtDlpBinary(string dataDirectory, HttpClient? http = null, ILogger? logger = null)
    {
        _dataDir = dataDirectory;
        // The default HttpClient timeout (100 s) covers the WHOLE body read — on a slow link a ~45 MB
        // binary gets cut off mid-stream and the truncated file poisons every later attempt. Rely on the
        // caller's CancellationToken instead.
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _log = logger ?? NullLogger.Instance;
    }

    public Task<string> ExtractJsonAsync(string url, CancellationToken cancellationToken)
        => ExtractJsonAsync(url, cookieFilePath: null, cancellationToken);

    public async Task<string> ExtractJsonAsync(string url, string? cookieFilePath, CancellationToken cancellationToken)
    {
        var exe = await EnsureYtDlpAsync(cancellationToken).ConfigureAwait(false);
        // YouTube drops all real formats unless yt-dlp can solve its JS "n challenge", which needs a JS
        // runtime (deno). Best-effort: extraction still works for most sites without it.
        var deno = await TryEnsureDenoAsync(cancellationToken).ConfigureAwait(false);

        // If the browser extension handed over a live session's cookies, try them FIRST — a live cookie jar
        // is far more reliable than reading a browser's on-disk store, which can be encrypted or keychain-
        // gated (and can hang). Only fall through if these cookies don't work (e.g. expired since capture).
        if (!string.IsNullOrEmpty(cookieFilePath))
        {
            _log.LogInformation("Trying extension-supplied cookies for {Url}", url);
            var (co, _, ccode) = await RunAsync(exe, BuildArgs(url, cookieFilePath, null, deno), cancellationToken)
                .ConfigureAwait(false);
            if (ccode == 0 && !string.IsNullOrWhiteSpace(co))
                return co;
            _log.LogWarning("Supplied cookies didn't work (yt-dlp exit {Code}); falling back", ccode); // never logs cookie values
        }

        // Try without cookies (works for public content and never touches browser data).
        var (stdout, stderr, exitCode) = await RunAsync(exe, BuildArgs(url, null, null, deno), cancellationToken)
            .ConfigureAwait(false);
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
            return stdout;

        // Some sites (notably YouTube) refuse anonymous extraction ("Sign in to confirm you're not a
        // bot") — retry with the cookies of each browser installed on this machine until one works.
        if (NeedsCookies(stderr))
        {
            foreach (var browser in CookieBrowsers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _log.LogInformation("Site requires sign-in — retrying yt-dlp with {Browser} cookies", browser);
                var (o, e, code) = await RunAsync(exe, BuildArgs(url, null, browser, deno), cancellationToken)
                    .ConfigureAwait(false);
                if (code == 0 && !string.IsNullOrWhiteSpace(o))
                    return o;
                // A missing/locked browser fails fast with a cookie-database error; keep trying the rest.
                _log.LogWarning("yt-dlp with {Browser} cookies exited {Code}: {Err}", browser, code, Tail(e));
                // Cookies got past the sign-in wall but the site returned no usable formats — on YouTube
                // that means the JS "n challenge" wasn't solved (deno missing), not a cookie problem.
                if (deno is null && MissingFormats(e))
                    throw NoJsRuntimeError();
            }
            throw new InvalidOperationException(
                "This site requires a signed-in browser session. Open the video once in your browser " +
                "while logged in, then try again.");
        }

        _log.LogWarning("yt-dlp exited {Code}: {Err}", exitCode, Tail(stderr));
        if (deno is null && MissingFormats(stderr))
            throw NoJsRuntimeError();
        throw new InvalidOperationException(
            $"Couldn't extract a video from this link. {FriendlyError(stderr)}".Trim());
    }

    // -J: dump a single JSON object, no download. --no-warnings keeps stdout clean JSON.
    // A supplied cookie FILE (--cookies) wins over reading a browser's on-disk store (--cookies-from-browser).
    internal static string BuildArgs(string url, string? cookieFile, string? cookieBrowser, string? denoPath) =>
        (denoPath is null ? "" : $"--js-runtimes \"deno:{denoPath}\" ")
        + (cookieFile is null ? "" : $"--cookies \"{cookieFile}\" ")
        + (cookieBrowser is null ? "" : $"--cookies-from-browser {cookieBrowser} ")
        + $"-J --no-warnings --no-playlist \"{url}\"";

    /// <summary>Resolve deno (yt-dlp's JS-challenge runtime): cached → PATH → downloaded on first use.
    /// Best-effort — returns null (and extraction proceeds without it) when provisioning fails.</summary>
    internal async Task<string?> TryEnsureDenoAsync(CancellationToken ct)
    {
        if (_denoResolved is not null) return _denoResolved.Length == 0 ? null : _denoResolved;

        var cached = DenoExePath(_dataDir);
        if (File.Exists(cached)) return _denoResolved = cached;

        var onPath = FindOnPath(Path.GetFileName(cached));
        if (onPath is not null) return _denoResolved = onPath;

        try
        {
            var url = DenoDownloadUrl;
            _log.LogInformation("deno not found; downloading {Url} into {Dir}", url, _dataDir);
            var archive = DenoArchivePath(_dataDir);
            Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
            await using (var fs = File.Create(archive))
            await using (var stream = await _http.GetStreamAsync(url, ct).ConfigureAwait(false))
                await stream.CopyToAsync(fs, ct).ConfigureAwait(false);
            await FinishDenoInstallAsync(ct).ConfigureAwait(false);
            return _denoResolved = cached;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Couldn't provision deno — YouTube formats may be unavailable");
            // Never leave a partial/corrupt archive behind: it would make every later attempt (here AND
            // the host's install-time fetch) extract garbage instead of re-downloading.
            try { File.Delete(DenoArchivePath(_dataDir)); } catch (IOException) { }
            _denoResolved = string.Empty; // don't retry every extraction
            return null;
        }
    }

    /// <summary>Declares yt-dlp and deno as <see cref="PluginBinaryDependency"/> entries so the host can
    /// fetch them (resumable, with progress) at plugin-install time instead of silently on first real use.
    /// Deno is included even though it's best-effort at runtime — pre-fetching it up front avoids a silent,
    /// unreported failure later.</summary>
    public IReadOnlyList<PluginBinaryDependency> GetDependencies() => new[]
    {
        new PluginBinaryDependency
        {
            Id = "yt-dlp",
            DisplayName = "yt-dlp",
            DownloadUrl = new Uri(ReleaseBase + AssetName),
            DownloadDestination = YtDlpExePath(_dataDir),
            IsAvailable = () => File.Exists(YtDlpExePath(_dataDir)) || FindOnPath(ExeName) is not null,
            FinishInstallAsync = ct =>
            {
                if (!OperatingSystem.IsWindows())
                    MakeExecutable(YtDlpExePath(_dataDir));
                return Task.CompletedTask;
            },
        },
        new PluginBinaryDependency
        {
            Id = "deno",
            DisplayName = "Deno",
            DownloadUrl = new Uri(DenoDownloadUrl),
            DownloadDestination = DenoArchivePath(_dataDir),
            IsAvailable = () => File.Exists(DenoExePath(_dataDir)) || FindOnPath(Path.GetFileName(DenoExePath(_dataDir))) is not null,
            FinishInstallAsync = FinishDenoInstallAsync,
        },
    };

    private static string DenoExePath(string dataDir) =>
        Path.Combine(dataDir, "deno-bin", OperatingSystem.IsWindows() ? "deno.exe" : "deno");

    private static string DenoArchivePath(string dataDir) => Path.Combine(dataDir, "deno-bin", "deno.zip");

    private static string DenoDownloadUrl =>
        "https://github.com/denoland/deno/releases/latest/download/" + DenoAssetName;

    private Task FinishDenoInstallAsync(CancellationToken ct)
    {
        var binDir = Path.Combine(_dataDir, "deno-bin");
        Directory.CreateDirectory(binDir);
        var archive = DenoArchivePath(_dataDir);
        try
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(archive, binDir, overwriteFiles: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A truncated/corrupt zip (interrupted download) must not survive — delete it so the next
            // attempt downloads fresh instead of failing on the same bad file forever.
            try { File.Delete(archive); } catch (IOException) { }
            throw new InvalidOperationException("The downloaded Deno archive was corrupt; it will be re-downloaded on the next attempt.", ex);
        }
        try { File.Delete(archive); } catch (IOException) { }
        if (!OperatingSystem.IsWindows())
            MakeExecutable(DenoExePath(_dataDir));
        return Task.CompletedTask;
    }

    private static string DenoAssetName
    {
        get
        {
            var arm = RuntimeInformation.OSArchitecture is Architecture.Arm64;
            if (OperatingSystem.IsWindows())
                return arm ? "deno-aarch64-pc-windows-msvc.zip" : "deno-x86_64-pc-windows-msvc.zip";
            if (OperatingSystem.IsMacOS())
                return arm ? "deno-aarch64-apple-darwin.zip" : "deno-x86_64-apple-darwin.zip";
            if (OperatingSystem.IsLinux())
                return arm ? "deno-aarch64-unknown-linux-gnu.zip" : "deno-x86_64-unknown-linux-gnu.zip";
            throw new PlatformNotSupportedException("No deno build configured for this OS.");
        }
    }

    /// <summary>Does this stderr say the site offered no usable formats? On YouTube this is the signature
    /// of an unsolved JS "n challenge" (no JS runtime), which hides every real format.</summary>
    internal static bool MissingFormats(string stderr) =>
        !string.IsNullOrWhiteSpace(stderr)
        && stderr.Contains("Requested format is not available", StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException NoJsRuntimeError() => new(
        "This video needs the Deno component, which isn't installed yet. It is downloaded " +
        "automatically — check your internet connection and try again in a minute.");

    /// <summary>Does this stderr indicate the site wants a signed-in session (worth a cookie retry)?</summary>
    internal static bool NeedsCookies(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return false;
        var lower = stderr.ToLowerInvariant();
        return lower.Contains("--cookies") || lower.Contains("sign in") || lower.Contains("log in")
               || lower.Contains("login required") || lower.Contains("age");
    }

    /// <summary>Browsers to source cookies from, most common first (yt-dlp fails fast when one is absent).</summary>
    private static IReadOnlyList<string> CookieBrowsers =>
        OperatingSystem.IsMacOS() ? new[] { "chrome", "safari", "brave", "edge", "firefox" }
        : OperatingSystem.IsWindows() ? new[] { "chrome", "edge", "brave", "firefox" }
        : new[] { "chrome", "chromium", "brave", "firefox" };

    private async Task<(string Stdout, string Stderr, int ExitCode)> RunAsync(
        string exe, string args, CancellationToken cancellationToken)
    {
        _log.LogInformation("Running yt-dlp: {Exe} {Args}", exe, args);

        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        Process proc;
        try
        {
            proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start yt-dlp.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "yt-dlp could not be started ({Exe})", exe);
            throw new InvalidOperationException(
                "Video extraction is unavailable: yt-dlp could not be started.", ex);
        }

        using (proc)
        {
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return (await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false),
                proc.ExitCode);
        }
    }

    /// <summary>Resolve yt-dlp: cached binary → system PATH → download on first use.</summary>
    public async Task<string> EnsureYtDlpAsync(CancellationToken cancellationToken)
    {
        if (_resolved is not null) return _resolved;

        var cached = YtDlpExePath(_dataDir);
        if (File.Exists(cached)) return _resolved = cached;

        var onPath = FindOnPath(ExeName);
        if (onPath is not null) return _resolved = onPath;

        _log.LogInformation("yt-dlp not found; downloading the latest build into {Dir}", _dataDir);
        return _resolved = await DownloadAsync(cached, cancellationToken).ConfigureAwait(false);
    }

    private static string YtDlpExePath(string dataDir) => Path.Combine(dataDir, "yt-dlp-bin", ExeName);

    private async Task<string> DownloadAsync(string targetExe, CancellationToken ct)
    {
        var url = ReleaseBase + AssetName;
        var binDir = Path.GetDirectoryName(targetExe)!;
        Directory.CreateDirectory(binDir);

        try
        {
            await using (var fs = File.Create(targetExe))
            await using (var stream = await _http.GetStreamAsync(url, ct).ConfigureAwait(false))
                await stream.CopyToAsync(fs, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Failed to download yt-dlp from {Url}", url);
            try { File.Delete(targetExe); } catch (IOException) { }
            throw new InvalidOperationException(
                "Video extraction is unavailable: yt-dlp could not be downloaded.", ex);
        }

        if (!OperatingSystem.IsWindows())
            MakeExecutable(targetExe);

        return targetExe;
    }

    private static void MakeExecutable(string path)
    {
        var psi = new ProcessStartInfo("chmod", $"+x \"{path}\"") { UseShellExecute = false, CreateNoWindow = true };
        try { Process.Start(psi)?.WaitForExit(); } catch { /* best effort */ }
    }

    private static string? FindOnPath(string exeName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar)) return null;
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var candidate = Path.Combine(dir, exeName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string ExeName => OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";

    /// <summary>The correct release asset for this OS/arch.</summary>
    private static string AssetName
    {
        get
        {
            if (OperatingSystem.IsWindows()) return "yt-dlp.exe";
            if (OperatingSystem.IsMacOS()) return "yt-dlp_macos"; // universal (arm64 + x64)
            if (OperatingSystem.IsLinux())
                return RuntimeInformation.OSArchitecture is Architecture.Arm64
                    ? "yt-dlp_linux_aarch64"
                    : "yt-dlp_linux";
            throw new PlatformNotSupportedException("No yt-dlp build configured for this OS.");
        }
    }

    private static string FriendlyError(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return string.Empty;
        var lower = stderr.ToLowerInvariant();
        if (lower.Contains("private") || lower.Contains("log in") || lower.Contains("age"))
            return "The video appears to be private, age-restricted, or unavailable.";
        if (lower.Contains("unsupported url") || lower.Contains("no video"))
            return "No video was found at this link.";
        return string.Empty;
    }

    private static string Tail(string s, int max = 600) => s.Length <= max ? s : s[^max..];
}
