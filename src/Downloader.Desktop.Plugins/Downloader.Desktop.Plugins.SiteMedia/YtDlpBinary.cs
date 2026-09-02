using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Downloader.Desktop.Plugins.SiteMedia;

/// <summary>
/// The real <see cref="IYtDlp"/>: locates yt-dlp (cached in the plugin's data dir → system PATH →
/// downloaded and checksum-verified on first use), then runs <c>yt-dlp -J</c> to dump a page's media
/// metadata as JSON.
/// <para>
/// Two rules this file exists to keep, both from issue #4: the tool is started from an ABSOLUTE path with
/// arguments we build ourselves — never through a command shell — and no browser profile, cookie store or
/// keychain is ever read. A signed-in session arrives only as a cookie file our own extension captured for
/// the one URL the user sent.
/// </para>
/// </summary>
public sealed class YtDlpBinary : IYtDlp
{
    // Self-contained builds from the yt-dlp GitHub releases, with the digests the project publishes beside
    // them. "latest" is intentional: extractors break as sites change, and a pinned build rots.
    private const string ReleaseBase = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/";
    private const string SumsAsset = "SHA2-256SUMS";

    private readonly string _dataDir;
    private readonly HttpClient _http;
    private readonly ILogger _log;
    private string? _resolved;
    private string? _denoResolved; // "" = provisioning failed, don't retry

    public YtDlpBinary(string dataDirectory, HttpClient? http = null, ILogger? logger = null)
    {
        _dataDir = dataDirectory;
        // The default HttpClient timeout (100 s) covers the WHOLE body read — on a slow link a ~45 MB
        // binary gets cut off mid-stream. Rely on the caller's CancellationToken instead.
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

        // A live session captured by our extension is tried FIRST — it is the only session this plugin
        // will ever have, and for a signed-in-only page it is the difference between working and not.
        if (!string.IsNullOrEmpty(cookieFilePath))
        {
            _log.LogInformation("Trying extension-supplied cookies for {Url}", url);
            var (co, cstderr, ccode) = await RunAsync(exe, BuildArgs(url, cookieFilePath, deno), cancellationToken)
                .ConfigureAwait(false);
            if (ccode == 0 && !string.IsNullOrWhiteSpace(co))
            {
                WarnAboutTokenGatedFormats(cstderr);
                return co;
            }
            _log.LogWarning("Supplied cookies didn't work (exit {Code}); trying anonymously", ccode); // never logs cookie values
        }

        // Anonymous — works for public content and touches no user data at all.
        var (stdout, stderr, exitCode) = await RunAsync(exe, BuildArgs(url, null, deno), cancellationToken)
            .ConfigureAwait(false);
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
        {
            WarnAboutTokenGatedFormats(stderr);
            return stdout;
        }

        // The cached tool would otherwise stay frozen forever while the sites it extracts change every few
        // weeks — a stale binary is the single most common cause of "this link used to work". Refresh only
        // after a failure, so the happy path pays nothing, then retry once.
        if (await TryRefreshYtDlpAsync(exe, cancellationToken).ConfigureAwait(false))
        {
            (stdout, stderr, exitCode) = await RunAsync(exe, BuildArgs(url, null, deno), cancellationToken)
                .ConfigureAwait(false);
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
                return stdout;
        }

        // x.com/Twitter: the guest-token GraphQL API intermittently returns no media for public posts.
        // The syndication API is a cookie-free public endpoint that still serves them.
        if (IsTwitter(url))
        {
            _log.LogInformation("Retrying {Url} via the Twitter syndication API (cookie-free)", url);
            var (so, se, scode) = await RunAsync(exe, BuildArgs(url, null, deno, SyndicationArgs), cancellationToken)
                .ConfigureAwait(false);
            if (scode == 0 && !string.IsNullOrWhiteSpace(so))
                return so;
            _log.LogWarning("Syndication API extraction also failed (exit {Code}): {Err}", scode, Tail(se));
        }

        _log.LogWarning("yt-dlp exited {Code}: {Err}", exitCode, Tail(stderr));
        if (deno is null && MissingFormats(stderr))
            throw new InvalidOperationException(NoJsRuntimeMessage);
        if (NeedsSession(stderr))
            throw new InvalidOperationException(NeedsSessionMessage(!string.IsNullOrEmpty(cookieFilePath)));
        throw new InvalidOperationException(
            $"Couldn't extract a video from this link. {FriendlyError(stderr)}".Trim());
    }

    // -J: dump one JSON object on stdout, no download. Warnings are deliberately NOT suppressed: they go
    // to stderr (stdout stays parseable JSON) and they are how YouTube's own reason for a later 403
    // reaches our log — "<client> client https formats require a GVS PO Token which was not provided.
    // They will be skipped as they may yield HTTP Error 403." Without them a refused link looks
    // inexplicable. There is deliberately
    // no --cookies-from-browser here or anywhere else: reading a browser's cookie store is exactly the
    // infostealer behaviour the extension exists to avoid (issue #4, guarded by NoShellSpawnTests).
    internal static string BuildArgs(string url, string? cookieFile, string? denoPath, string? extractorArgs = null) =>
        (denoPath is null ? "" : $"--js-runtimes \"deno:{denoPath}\" ")
        + (cookieFile is null ? "" : $"--cookies \"{cookieFile}\" ")
        + (string.IsNullOrEmpty(extractorArgs) ? "" : $"--extractor-args \"{extractorArgs}\" ")
        + $"-J --no-playlist \"{url}\"";

    /// <summary>Extractor-args routing x.com/Twitter through the cookie-free public syndication API.</summary>
    internal const string SyndicationArgs = "twitter:api=syndication";

    /// <summary>Logs yt-dlp's own explanation when the formats it just handed back are the kind YouTube
    /// serves only against a token this app cannot mint — the reason a download that was planned from a
    /// perfectly successful extraction then dies on its first request with 403.</summary>
    private void WarnAboutTokenGatedFormats(string stderr)
    {
        if (MentionsMissingToken(stderr))
            _log.LogWarning("YouTube wants a PO token for these formats: {Warning}", Tail(stderr));
    }

    /// <summary>True when yt-dlp said the formats need a GVS PO token it did not have.</summary>
    internal static bool MentionsMissingToken(string stderr) =>
        !string.IsNullOrEmpty(stderr)
        && stderr.Contains("PO Token", StringComparison.OrdinalIgnoreCase)
        && stderr.Contains("not provided", StringComparison.OrdinalIgnoreCase);

    /// <summary>Extractor-args pinning YouTube to one player client.</summary>
    internal static string YouTubeClientArgs(string client) => $"youtube:player_client={client}";

    /// <summary>One extraction pinned to a specific player client, with no fallback ladder: the caller
    /// (<see cref="SiteMediaResolver"/>) is already walking a list of clients and decides what to try
    /// next, so a failure here is just "that client didn't work".</summary>
    public async Task<string> ExtractJsonAsync(
        string url, string? cookieFilePath, string? playerClient, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playerClient))
            return await ExtractJsonAsync(url, cookieFilePath, cancellationToken).ConfigureAwait(false);

        var exe = await EnsureYtDlpAsync(cancellationToken).ConfigureAwait(false);
        var deno = await TryEnsureDenoAsync(cancellationToken).ConfigureAwait(false);
        _log.LogInformation("Re-extracting {Url} through the {Client} player client", url, playerClient);

        var args = BuildArgs(url, cookieFilePath, deno, YouTubeClientArgs(playerClient));
        var (stdout, stderr, exitCode) = await RunAsync(exe, args, cancellationToken).ConfigureAwait(false);
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
        {
            WarnAboutTokenGatedFormats(stderr);
            return stdout;
        }

        _log.LogWarning("The {Client} player client failed (exit {Code}): {Err}", playerClient, exitCode, Tail(stderr));
        throw new InvalidOperationException($"Couldn't extract this link through {playerClient}.");
    }

    /// <summary>True for an x.com / twitter.com page (incl. subdomains), without matching look-alikes
    /// such as <c>x.com.evil.com</c>.</summary>
    internal static bool IsTwitter(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        var host = u.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host[4..];
        return host.Equals("x.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("twitter.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".x.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".twitter.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Does this stderr say the site wants a signed-in session?</summary>
    internal static bool NeedsSession(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return false;
        var lower = stderr.ToLowerInvariant();
        return lower.Contains("--cookies") || lower.Contains("sign in") || lower.Contains("log in")
               || lower.Contains("login required") || lower.Contains("age")
               // x.com surfaces "anonymous request saw no media" as this rather than a sign-in error.
               || lower.Contains("no video could be found in this tweet");
    }

    /// <summary>What to tell the user when the site wants a session. Deliberately never "sign in to the
    /// site": the people who hit this are already signed in — what is missing is the session reaching the
    /// app, which is what sending the page from the extension does.</summary>
    internal static string NeedsSessionMessage(bool hadCookies) => hadCookies
        ? "This site hands the video only to a signed-in session, and the session sent with this link was "
          + "not accepted — it has most likely expired. Reload the page in your browser and send it again "
          + "from the Downloader extension."
        : "This site hands the video only to a signed-in session, and this link was added without one. "
          + "Send the page from the Downloader browser extension instead — it passes your existing browser "
          + "session along with the link.";

    /// <summary>Does this stderr say the site offered no usable formats? On YouTube that is the signature
    /// of an unsolved JS "n challenge" (no JS runtime), which hides every real format.</summary>
    internal static bool MissingFormats(string stderr) =>
        !string.IsNullOrWhiteSpace(stderr)
        && stderr.Contains("Requested format is not available", StringComparison.OrdinalIgnoreCase);

    internal const string NoJsRuntimeMessage =
        "This video needs the Deno component, which isn't installed yet. It is downloaded automatically — "
        + "check your internet connection and try again in a minute.";

    private async Task<(string Stdout, string Stderr, int ExitCode)> RunAsync(
        string exe, string args, CancellationToken cancellationToken)
    {
        _log.LogInformation("Running the extraction tool: {Exe} {Args}", exe, args);

        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false, // absolute path, our own arguments, no shell — issue #4
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

    /// <summary>Resolve yt-dlp: cached binary → system PATH → download + verify on first use.</summary>
    public async Task<string> EnsureYtDlpAsync(CancellationToken cancellationToken)
    {
        if (_resolved is not null) return _resolved;

        var cached = YtDlpExePath(_dataDir);
        if (ToolFile.IsUsable(cached)) return _resolved = cached;
        // Present but unusable = an interrupted download from an earlier run. Clear it out, otherwise
        // every extraction fails with "yt-dlp could not be started" and nothing ever repairs it.
        if (File.Exists(cached))
        {
            _log.LogWarning("The cached yt-dlp at {Path} is incomplete — downloading it again", cached);
            ToolFile.DeleteIfPresent(cached);
        }

        var onPath = ToolFile.FindOnPath(ExeName);
        if (onPath is not null) return _resolved = onPath;

        _log.LogInformation("yt-dlp not found; downloading the latest build into {Dir}", _dataDir);
        return _resolved = await DownloadAsync(cached, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> DownloadAsync(string targetExe, CancellationToken ct)
    {
        var url = ReleaseBase + AssetName;

        try
        {
            await ToolFile.DownloadToAsync(_http, url, targetExe, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Failed to download yt-dlp from {Url}", url);
            throw new InvalidOperationException(
                "Video extraction is unavailable: yt-dlp could not be downloaded.", ex);
        }

        await VerifyYtDlpAsync(targetExe, ct).ConfigureAwait(false);
        ToolFile.MakeExecutable(targetExe);
        return targetExe;
    }

    /// <summary>Check the downloaded tool against the digest yt-dlp publishes for that asset, BEFORE it is
    /// made executable or run. A mismatch deletes it and fails the download.</summary>
    internal async Task VerifyYtDlpAsync(string path, CancellationToken ct)
    {
        var expected = await FetchExpectedSumAsync(ReleaseBase + SumsAsset, AssetName, allowSingleEntry: false, ct).ConfigureAwait(false);
        if (expected is null)
        {
            // The publisher's sums file is unreachable or does not list this asset. Refusing to run an
            // unverified binary is the whole point, so this fails rather than proceeding.
            ToolFile.DeleteIfPresent(path);
            throw new InvalidOperationException(
                "Video extraction is unavailable: the published checksum for yt-dlp could not be read, so "
                + "the downloaded copy was discarded rather than run unverified.");
        }

        await ToolChecksum.VerifyOrDiscardAsync(path, expected, "yt-dlp", ct).ConfigureAwait(false);
        _log.LogInformation("Verified the downloaded yt-dlp against its published sha256");
    }

    private async Task<string?> FetchExpectedSumAsync(
        string sumsUrl, string assetName, bool allowSingleEntry, CancellationToken ct)
    {
        try
        {
            var text = await _http.GetStringAsync(sumsUrl, ct).ConfigureAwait(false);
            return ToolChecksum.ParseSums(text, assetName, allowSingleEntry);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Couldn't read the published checksums at {Url}", sumsUrl);
            return null;
        }
    }

    private static string YtDlpExePath(string dataDir) => Path.Combine(dataDir, "yt-dlp-bin", ExeName);

    /// <summary>How old the cached tool may get before a FAILED extraction is worth retrying with a freshly
    /// self-updated build. Short, because extractors break within weeks; the check only runs after
    /// something already failed, so it costs nothing while links keep working.</summary>
    internal static readonly TimeSpan StaleAfter = TimeSpan.FromDays(3);

    internal static bool IsStale(DateTime lastWriteUtc, DateTime nowUtc) => nowUtc - lastWriteUtc >= StaleAfter;

    private bool _refreshTried;

    /// <summary>Self-update our cached copy (<c>--update-to stable</c>) when it has gone stale, and report
    /// whether the binary actually changed. Best-effort: a yt-dlp found on PATH is the user's (or the
    /// distro's) to update and is never touched.</summary>
    private async Task<bool> TryRefreshYtDlpAsync(string exe, CancellationToken ct)
    {
        if (_refreshTried) return false;
        _refreshTried = true;

        if (!string.Equals(exe, YtDlpExePath(_dataDir), StringComparison.Ordinal)) return false;

        try
        {
            if (!IsStale(File.GetLastWriteTimeUtc(exe), DateTime.UtcNow)) return false;

            _log.LogInformation("Extraction failed and yt-dlp is stale — updating it before retrying");
            var (stdout, stderr, code) = await RunAsync(exe, "--update-to stable", ct).ConfigureAwait(false);
            // Mark it checked either way so a run of failing links doesn't re-update on every attempt.
            try { File.SetLastWriteTimeUtc(exe, DateTime.UtcNow); } catch (IOException) { }

            var updated = code == 0 && stdout.Contains("Updated yt-dlp", StringComparison.OrdinalIgnoreCase);
            if (!updated)
                _log.LogInformation("yt-dlp was already current (exit {Code}): {Out}", code, Tail(stdout + stderr, 200));
            return updated;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Couldn't update yt-dlp — continuing with the installed build");
            return false;
        }
    }

    /// <summary>Resolve deno (the JS-challenge runtime): cached → PATH → downloaded and verified on first
    /// use. Best-effort — returns null (and extraction proceeds without it) when provisioning fails.</summary>
    internal async Task<string?> TryEnsureDenoAsync(CancellationToken ct)
    {
        if (_denoResolved is not null) return _denoResolved.Length == 0 ? null : _denoResolved;

        var cached = DenoExePath(_dataDir);
        if (ToolFile.IsUsable(cached)) return _denoResolved = cached;
        if (File.Exists(cached))
        {
            _log.LogWarning("The cached deno at {Path} is incomplete — downloading it again", cached);
            ToolFile.DeleteIfPresent(cached);
        }

        var onPath = ToolFile.FindOnPath(Path.GetFileName(cached));
        if (onPath is not null) return _denoResolved = onPath;

        try
        {
            var url = DenoDownloadUrl;
            _log.LogInformation("deno not found; downloading {Url} into {Dir}", url, _dataDir);
            await ToolFile.DownloadToAsync(_http, url, DenoArchivePath(_dataDir), ct).ConfigureAwait(false);
            await FinishDenoInstallAsync(ct).ConfigureAwait(false);
            return _denoResolved = cached;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Couldn't provision deno — YouTube formats may be unavailable");
            // Never leave a partial/corrupt archive behind: it would make every later attempt (here AND
            // the host's install-time fetch) extract garbage instead of re-downloading.
            ToolFile.DeleteIfPresent(DenoArchivePath(_dataDir));
            _denoResolved = string.Empty; // don't retry every extraction
            return null;
        }
    }

    /// <summary>Declares the tools as <see cref="PluginBinaryDependency"/> entries so the host fetches them
    /// (resumable, with progress) at plugin-install time instead of silently on first real use. The
    /// checksum check runs in <c>FinishInstallAsync</c>, i.e. before anything is made executable.</summary>
    public IReadOnlyList<PluginBinaryDependency> GetDependencies() => new[]
    {
        new PluginBinaryDependency
        {
            Id = "yt-dlp",
            DisplayName = "yt-dlp",
            DownloadUrl = new Uri(ReleaseBase + AssetName),
            DownloadDestination = YtDlpExePath(_dataDir),
            // Usable, not merely present: an interrupted download leaves a truncated file here, and
            // "it exists" would mark the dependency installed forever (the host then never refetches).
            IsAvailable = () => ToolFile.IsUsable(YtDlpExePath(_dataDir)) || ToolFile.FindOnPath(ExeName) is not null,
            FinishInstallAsync = async ct =>
            {
                await VerifyYtDlpAsync(YtDlpExePath(_dataDir), ct).ConfigureAwait(false);
                ToolFile.MakeExecutable(YtDlpExePath(_dataDir));
            },
        },
        new PluginBinaryDependency
        {
            Id = "deno",
            DisplayName = "Deno",
            DownloadUrl = new Uri(DenoDownloadUrl),
            DownloadDestination = DenoArchivePath(_dataDir),
            IsAvailable = () => ToolFile.IsUsable(DenoExePath(_dataDir))
                                || ToolFile.FindOnPath(Path.GetFileName(DenoExePath(_dataDir))) is not null,
            FinishInstallAsync = FinishDenoInstallAsync,
        },
    };

    private static string DenoExePath(string dataDir) =>
        Path.Combine(dataDir, "deno-bin", OperatingSystem.IsWindows() ? "deno.exe" : "deno");

    private static string DenoArchivePath(string dataDir) => Path.Combine(dataDir, "deno-bin", "deno.zip");

    private static string DenoDownloadUrl =>
        "https://github.com/denoland/deno/releases/latest/download/" + DenoAssetName;

    private async Task FinishDenoInstallAsync(CancellationToken ct)
    {
        var binDir = Path.Combine(_dataDir, "deno-bin");
        Directory.CreateDirectory(binDir);
        var archive = DenoArchivePath(_dataDir);

        // Verify the ARCHIVE before extracting it — an unverified zip is a code-execution surface too.
        var expected = await FetchExpectedSumAsync(DenoDownloadUrl + ".sha256sum", DenoAssetName, allowSingleEntry: true, ct)
            .ConfigureAwait(false);
        if (expected is null)
        {
            ToolFile.DeleteIfPresent(archive);
            throw new InvalidOperationException(
                "The published checksum for Deno could not be read, so the download was discarded rather "
                + "than extracted unverified.");
        }
        await ToolChecksum.VerifyOrDiscardAsync(archive, expected, "Deno archive", ct).ConfigureAwait(false);

        try
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(archive, binDir, overwriteFiles: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A truncated/corrupt zip must not survive — delete it so the next attempt downloads fresh
            // instead of failing on the same bad file forever.
            ToolFile.DeleteIfPresent(archive);
            throw new InvalidOperationException(
                "The downloaded Deno archive was corrupt; it will be re-downloaded on the next attempt.", ex);
        }
        ToolFile.DeleteIfPresent(archive);
        ToolFile.MakeExecutable(DenoExePath(_dataDir));
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


    private static string ExeName => OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";

    /// <summary>The correct release asset for this OS/arch.</summary>
    internal static string AssetName
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
        if (lower.Contains("live") && lower.Contains("stream"))
            return "This looks like a live stream, which can't be downloaded as a file.";
        if (lower.Contains("private") || lower.Contains("unavailable"))
            return "The video appears to be private or unavailable.";
        if (lower.Contains("drm") || lower.Contains("protected"))
            return "The video is protected and can't be downloaded.";
        if (lower.Contains("unsupported url") || lower.Contains("no video"))
            return "No video was found at this link.";
        return string.Empty;
    }

    private static string Tail(string s, int max = 600) => s.Length <= max ? s : s[^max..];
}
