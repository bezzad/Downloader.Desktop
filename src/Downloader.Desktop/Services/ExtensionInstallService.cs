using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform;
using Downloader.Desktop.Models;

namespace Downloader.Desktop.Services;

/// <summary>What an install attempt did, and what to tell the user if it didn't work.</summary>
public sealed class ExtensionInstallResult
{
    public bool Success { get; private init; }
    public string Error { get; private init; }

    /// <summary>Where the build was unpacked. This path is what the user pastes into their browser.</summary>
    public string Path { get; private init; }

    public string Version { get; private init; }

    public static ExtensionInstallResult Ok(string path, string version)
        => new() { Success = true, Path = path, Version = version };

    public static ExtensionInstallResult Fail(string error)
        => new() { Success = false, Error = error };
}

/// <summary>What is currently unpacked for a target, as recorded at install time.</summary>
public sealed class InstalledExtension
{
    public string Target { get; init; }
    public string Version { get; init; }
    public DateTimeOffset InstalledAt { get; init; }
}

/// <summary>
/// The unpacked copy of a target as the install dialog needs it: WHERE it is (the folder the user hands
/// to their browser) and WHICH version it is. Both facts behind one value so the dialog reaches the
/// filesystem through a single seam — computing the path separately meant a test could stub the version
/// but not the path, and the view model then read the developer's real config folder.
/// </summary>
public sealed record InstalledCopy(string Path, string Version);

/// <summary>
/// Downloads a browser-extension build, verifies it, and unpacks it where the user's browser can load it.
///
/// <para><b>The app never installs anything into a browser</b> — no browser is capable of accepting a
/// locally installed unsigned extension into a normal profile, and the one mechanism that would work
/// (an enterprise-policy write) is the browser-hijacker signature, scored higher for being elevated. So
/// this service's whole job is to remove the steps a user should not have to do by hand: fetch the right
/// build, prove it is the published one, and put it somewhere stable. The install itself happens in the
/// browser, by the user.</para>
///
/// <para>Everything written stays under <see cref="InstallRoot"/>. Nothing is marked executable, no
/// process is spawned (extraction is <see cref="ZipFile"/>, not a shell or <c>tar</c>), and no browser
/// profile, extension directory or policy location is ever a write target.</para>
/// </summary>
public static class ExtensionInstallService
{
    /// <summary>Test seam: installing into the real folder would leave a build in the developer's own
    /// app data and could clobber an extension they are actually using. The app never sets it.</summary>
    internal static string InstallRootOverride { get; set; }

    /// <summary>Where unpacked builds live — beside <c>plugins/</c> in the app's own data directory.</summary>
    public static string InstallRoot => InstallRootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Downloader", "extension");

    /// <summary>
    /// The folder a target is unpacked into. <b>It must not change between installs:</b> a browser
    /// identifies a manually loaded extension by its absolute directory path, so a fresh path each time
    /// would mean a fresh identity and an empty settings store on every update — and a temp folder would
    /// break the extension the next time the OS cleaned temp.
    /// </summary>
    public static string TargetPath(string targetId) => Path.Combine(InstallRoot, SafeName(targetId));

    private const string MarkerFile = "installed.json";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        // No overall timeout: the cancellation token governs. A whole-request timeout truncates a slow
        // download and then reports a checksum mismatch, which is the wrong diagnosis (see the plugin
        // binaries' Timeout.InfiniteTimeSpan for the same reason).
        var c = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Downloader.Desktop", ver));
        return c;
    }

    /// <summary>
    /// Download → <b>verify the checksum</b> → validate every archive entry → unpack to
    /// <see cref="TargetPath"/>. The verification is a hard gate: a mismatch leaves nothing extracted and
    /// does not disturb a previously installed copy.
    /// </summary>
    public static async Task<ExtensionInstallResult> InstallAsync(ExtensionCatalogEntry entry,
        IProgress<double> progress = null, CancellationToken ct = default)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.AssetUrl))
            return ExtensionInstallResult.Fail("There is no build to install for this browser.");
        if (string.IsNullOrWhiteSpace(entry.Sha256))
            return ExtensionInstallResult.Fail("This build has no checksum to verify against, so it will not be installed.");

        var tmp = Path.Combine(Path.GetTempPath(), $"downloader-ext-{Guid.NewGuid():N}.zip");
        try
        {
            if (!await DownloadAsync(entry.AssetUrl, tmp, progress, ct).ConfigureAwait(false))
                return ExtensionInstallResult.Fail("Could not download the extension. Please check your connection and try again.");

            // --- Security gate: verify BEFORE anything is extracted ---
            string actual;
            try { actual = await PluginManager.ComputeSha256Async(tmp, ct).ConfigureAwait(false); }
            catch (Exception ex) { return ExtensionInstallResult.Fail($"Could not read the download: {ex.Message}"); }

            if (!string.Equals(actual, entry.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                return ExtensionInstallResult.Fail(
                    "The download could not be verified (checksum mismatch). Nothing was installed — please try again.");

            var unsafeEntry = FindUnsafeEntry(tmp, out var readError);
            if (readError != null)
                return ExtensionInstallResult.Fail($"Could not read the download: {readError}");
            if (unsafeEntry != null)
                return ExtensionInstallResult.Fail(
                    $"The download contains an unexpected file path ('{unsafeEntry}') and was not installed.");

            return Unpack(tmp, entry, ct);
        }
        catch (OperationCanceledException)
        {
            return ExtensionInstallResult.Fail("The install was cancelled.");
        }
        catch (Exception ex)
        {
            AppLog.Error("Installing the browser extension failed", ex);
            return ExtensionInstallResult.Fail($"Could not install the extension: {ex.Message}");
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    /// <summary>
    /// Staged unpack: extract to <c>&lt;target&gt;.new</c>, then swap it in. An interrupted extraction
    /// therefore leaves the previous working copy in place rather than a half-extracted folder the browser
    /// would refuse to load.
    /// </summary>
    private static ExtensionInstallResult Unpack(string zipPath, ExtensionCatalogEntry entry, CancellationToken ct)
    {
        var target = TargetPath(entry.Id);
        var staging = target + ".new";

        try
        {
            Directory.CreateDirectory(InstallRoot);
            // A leftover from an interrupted attempt is not a state to preserve.
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);

            ZipFile.ExtractToDirectory(zipPath, staging, overwriteFiles: true);
            ct.ThrowIfCancellationRequested();

            File.WriteAllText(Path.Combine(staging, MarkerFile), JsonSerializer.Serialize(new InstalledExtension
            {
                Target = entry.Id,
                Version = entry.Version,
                InstalledAt = DateTimeOffset.Now,
            }));

            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
            Directory.Move(staging, target);

            return ExtensionInstallResult.Ok(target, entry.Version);
        }
        catch (Exception ex)
        {
            // Leave `target` exactly as it was; drop only our own staging folder.
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch { /* best-effort */ }

            AppLog.Error($"Unpacking the browser extension to '{target}' failed", ex);
            return ExtensionInstallResult.Fail($"Could not unpack the extension: {ex.Message}");
        }
    }

    /// <summary>
    /// The files bundled with the app, in the order the extension needs them. Kept in step with
    /// <c>COMMON</c> in <c>scripts/build-extension.sh</c> — a file that ships in the zip but not here
    /// would produce a bundled install a browser refuses to load, so a test compares the two lists.
    /// </summary>
    internal static readonly string[] BundledFiles =
    {
        "background.js", "common.js",
        "popup.html", "popup.css", "popup.js",
        "options.html", "options.css", "options.js",
        "icons/icon16.png", "icons/icon48.png", "icons/icon128.png",
    };

    /// <summary>
    /// Installs the copy that ships inside the app, for when the release catalog cannot be reached — or
    /// does not carry a build yet, which is the state of every release published before this feature
    /// existed. <b>This is what makes the installer work on a fresh machine, offline, and on the very
    /// release that introduces it.</b>
    ///
    /// <para>No checksum is involved and none is needed: these bytes came out of the application binary
    /// the user already trusts, not off the network. The catalog path keeps its hard verification gate.</para>
    /// </summary>
    public static ExtensionInstallResult InstallBundled(string targetId, bool gecko)
    {
        var target = TargetPath(targetId);
        var staging = target + ".new";
        try
        {
            Directory.CreateDirectory(InstallRoot);
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);

            // A gecko browser needs its own manifest, under the name every browser looks for.
            var manifest = gecko ? "manifest.firefox.json" : "manifest.json";
            CopyBundled(manifest, Path.Combine(staging, "manifest.json"));
            foreach (var file in BundledFiles)
                CopyBundled(file, Path.Combine(staging, file.Replace('/', Path.DirectorySeparatorChar)));

            var version = ReadBundledVersion(Path.Combine(staging, "manifest.json"));
            File.WriteAllText(Path.Combine(staging, MarkerFile), JsonSerializer.Serialize(new InstalledExtension
            {
                Target = targetId,
                Version = version,
                InstalledAt = DateTimeOffset.Now,
            }));

            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
            Directory.Move(staging, target);
            return ExtensionInstallResult.Ok(target, version);
        }
        catch (Exception ex)
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch { /* best-effort */ }
            AppLog.Error($"Installing the bundled extension to '{target}' failed", ex);
            return ExtensionInstallResult.Fail($"Could not unpack the extension: {ex.Message}");
        }
    }

    /// <summary>The version of the copy bundled with this app.</summary>
    public static string BundledVersion()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Downloader.Desktop/Assets/extension/manifest.json"));
            using var doc = JsonDocument.Parse(stream);
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    private static void CopyBundled(string name, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var source = AssetLoader.Open(new Uri($"avares://Downloader.Desktop/Assets/extension/{name}"));
        using var dest = File.Create(destination);
        source.CopyTo(dest);
    }

    private static string ReadBundledVersion(string manifestPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// What is unpacked for a target right now, or null. A missing or unreadable marker reads as "nothing
    /// installed" — it is a convenience record, never a source of truth about the browser.
    /// </summary>
    public static InstalledExtension ReadInstalled(string targetId)
    {
        try
        {
            var marker = Path.Combine(TargetPath(targetId), MarkerFile);
            if (!File.Exists(marker))
                return null;
            return JsonSerializer.Deserialize<InstalledExtension>(File.ReadAllText(marker));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The unpacked copy for a target — its folder and its version — or null when nothing is installed.
    /// One call for both, so the dialog needs exactly one seam to be testable (see <see cref="InstalledCopy"/>).
    /// </summary>
    public static InstalledCopy ReadInstalledCopy(string targetId)
    {
        var installed = ReadInstalled(targetId);
        return installed == null ? null : new InstalledCopy(TargetPath(targetId), installed.Version);
    }

    /// <summary>
    /// The first archive entry whose path would escape the destination, or null when every entry is safe.
    /// Rejects <c>..</c> segments, rooted paths and drive-qualified paths, and re-checks the resolved full
    /// path — a zip is untrusted input even after its checksum matches, because the checksum only proves it
    /// is the file the catalog named.
    /// </summary>
    internal static string FindUnsafeEntry(string zipPath, out string readError)
    {
        readError = null;
        try
        {
            var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "downloader-ext-probe"));
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var e in zip.Entries)
            {
                var name = e.FullName;
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                if (name.Contains("..", StringComparison.Ordinal)
                    || name.StartsWith('/') || name.StartsWith('\\')
                    || (name.Length > 1 && name[1] == ':'))
                    return name;

                var resolved = Path.GetFullPath(Path.Combine(root, name));
                if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && resolved != root)
                    return name;
            }
            return null;
        }
        catch (Exception ex)
        {
            readError = ex.Message;
            return null;
        }
    }

    private static async Task<bool> DownloadAsync(string url, string destPath,
        IProgress<double> progress, CancellationToken ct)
    {
        try
        {
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return false;

            var total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = File.Create(destPath);

            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                read += n;
                if (total > 0)
                    progress?.Report(Math.Min(1.0, (double)read / total));
            }
            progress?.Report(1.0);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* a leftover temp file is harmless */ }
    }

    /// <summary>A target id comes from a release asset, so keep it to a plain folder name.</summary>
    private static string SafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "unknown";
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
        var chars = new List<char>(name.Length);
        foreach (var c in name)
            chars.Add(invalid.Contains(c) || c is '.' or ' ' ? '-' : c);
        return new string(chars.ToArray());
    }
}
