using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Downloader.Desktop.Services;

/// <summary>A newer release found on GitHub.</summary>
public sealed class UpdateInfo
{
    public string Version { get; init; }      // e.g. "1.1.0"
    public string Tag { get; init; }          // e.g. "v1.1.0"
    public string AssetUrl { get; init; }     // platform archive download URL (may be null)
    public string AssetName { get; init; }    // archive file name (may be null)
    public string ReleaseUrl { get; init; }   // html page for the release
}

/// <summary>
/// Checks GitHub Releases for a newer version and (optionally) downloads the platform archive in the
/// background via the Downloader engine, then relaunches into it. Version comparison uses the
/// assembly's major.minor.patch (<see cref="Assembly.GetName"/>), which tracks <c>VersionPrefix</c> and
/// ignores the auto build/revision so a tag like <c>v1.1.0</c> compares cleanly.
/// </summary>
public static class UpdateService
{
    private const string Owner = "bezzad";
    private const string Repo = "Downloader.Desktop";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Downloader.Desktop", CurrentVersion.ToString()));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    /// <summary>The running app version as major.minor.patch (build/revision ignored).</summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? new Version(v.Major, v.Minor, v.Build) : new Version(1, 0, 0);

    /// <summary>Returns the newer release, or null if none / on any error.</summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(
        Justification = "Network: queries the GitHub releases API. The version compare and asset selection it depends on are covered in UpdateStackTests.")]
    public static async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (!IsNewer(tag, CurrentVersion))
                return null;

            var releaseUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() : null;
            var (assetUrl, assetName) = FindAsset(root);

            return new UpdateInfo
            {
                Version = Normalize(tag)?.ToString(),
                Tag = tag,
                AssetUrl = assetUrl,
                AssetName = assetName,
                ReleaseUrl = releaseUrl
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True if the release tag represents a version strictly newer than <paramref name="current"/>.</summary>
    public static bool IsNewer(string tag, Version current)
    {
        var remote = Normalize(tag);
        return remote != null && remote > current;
    }

    /// <summary>Parses "v1.2.3" / "1.2" into a 3-part <see cref="Version"/> (missing parts = 0).</summary>
    public static Version Normalize(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;
        var s = tag.Trim().TrimStart('v', 'V');
        var parts = s.Split('.');
        int Get(int i) => i < parts.Length && int.TryParse(parts[i], out var n) ? n : 0;
        if (parts.Length == 0 || !int.TryParse(parts[0], out _))
            return null;
        return new Version(Get(0), Get(1), Get(2));
    }

    /// <summary>The release-asset file name for the current OS/arch (matches release.yml naming).</summary>
    public static string ExpectedAssetName()
    {
        var rid = RuntimeInformation.RuntimeIdentifier; // e.g. "linux-x64"
        if (OperatingSystem.IsWindows()) return $"Downloader-{Map(rid, "win-x64")}.zip";
        if (OperatingSystem.IsMacOS()) return $"Downloader-{Map(rid, "osx-x64")}.tar.gz";
        return $"Downloader-{Map(rid, "linux-x64")}.tar.gz";

        static string Map(string rid, string fallback) =>
            string.IsNullOrWhiteSpace(rid) || rid.StartsWith("unknown") ? fallback : rid;
    }

    /// <summary>
    /// Picks this platform's archive out of a GitHub release payload. Internal rather than private so
    /// the asset-matching can be tested against a real release JSON shape without a network call —
    /// picking the wrong asset (or none) is what silently downgrades an update to "just open the
    /// release page".
    /// </summary>
    internal static (string url, string name) FindAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return (null, null);

        var want = ExpectedAssetName();
        foreach (var a in assets.EnumerateArray())
        {
            var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.Equals(name, want, StringComparison.OrdinalIgnoreCase) &&
                a.TryGetProperty("browser_download_url", out var u))
                return (u.GetString(), name);
        }
        return (null, null);
    }

    /// <summary>
    /// Replaces the current install with the already-downloaded archive and relaunches. Spawns a
    /// detached OS script that waits for this process to exit, extracts over the app folder, and starts
    /// the new build. Caller should shut the app down right after this returns true.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(
        Justification = "Spawns a detached swap script that overwrites the running installation and relaunches it. The generated scripts themselves are covered in UpdateStackTests.")]
    public static bool ApplyDownloadedArchive(string archivePath)
    {
        try
        {
            var exe = Environment.ProcessPath;
            var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(archivePath))
                return false;

            var pid = Environment.ProcessId;
            if (OperatingSystem.IsWindows())
                return RunDetached("cmd.exe", $"/c \"{WriteWindowsScript(archivePath, appDir, exe, pid)}\"");

            // macOS ships a .app BUNDLE (the archive contains "Downloader.app"). appDir is
            // <Bundle>.app/Contents/MacOS, so extracting into it would nest a new app inside the old one
            // and relaunch the OLD binary → re-detects the update → infinite loop. Replace the whole
            // bundle instead.
            if (OperatingSystem.IsMacOS())
            {
                var contents = Path.GetDirectoryName(appDir);          // …/Contents
                var bundle = Path.GetDirectoryName(contents);          // …/Downloader.app
                if (bundle != null && bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                    return RunDetached("/bin/bash", WriteMacScript(archivePath, bundle, pid));
            }

            return RunDetached("/bin/bash", WriteUnixScript(archivePath, appDir, exe, pid));
        }
        catch
        {
            return false;
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(
        Justification = "Starts the detached updater process.")]
    private static bool RunDetached(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true };
        return Process.Start(psi) != null;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(
        Justification = "Writes the swap script to a temp path; the script BODY it delegates to is covered in UpdateStackTests.")]
    private static string WriteUnixScript(string archive, string appDir, string exe, int pid)
    {
        var script = Path.Combine(Path.GetTempPath(), $"downloader-update-{pid}.sh");
        File.WriteAllText(script, BuildUnixScript(archive, appDir, exe, pid));
        return script;
    }

    /// <summary>The Linux swap script body. Split out from the file write (like
    /// <see cref="BuildWindowsScript"/>) so the parts that have actually broken before are testable:
    /// the archive-type branch, and the detached relaunch.</summary>
    internal static string BuildUnixScript(string archive, string appDir, string exe, int pid)
    {
        var extract = archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? $"unzip -o \"{archive}\" -d \"{appDir}\""
            : $"tar -xzf \"{archive}\" -C \"{appDir}\"";
        // trap '' HUP keeps the swapper alive past the parent's exit; relaunch via setsid (fallback
        // nohup) so the NEW app runs in its own session and isn't torn down with the old process group —
        // that detachment is what makes "restart to update" actually relaunch.
        return
            "#!/bin/bash\n" +
            "trap '' HUP\n" +
            $"while kill -0 {pid} 2>/dev/null; do sleep 0.5; done\n" +
            $"{extract}\n" +
            $"chmod +x \"{exe}\" 2>/dev/null\n" +
            $"rm -f \"{archive}\"\n" +
            $"if command -v setsid >/dev/null 2>&1; then setsid \"{exe}\" >/dev/null 2>&1 & else nohup \"{exe}\" >/dev/null 2>&1 & fi\n";
    }

    /// <summary>macOS: replace the whole <c>.app</c> bundle (the archive contains <c>Downloader.app</c>),
    /// then relaunch with <c>open</c>. Fixes the "restart reopens the OLD version → re-downloads → loop".</summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(
        Justification = "Writes the swap script to a temp path; the script BODY it delegates to is covered in UpdateStackTests.")]
    private static string WriteMacScript(string archive, string bundle, int pid)
    {
        var script = Path.Combine(Path.GetTempPath(), $"downloader-update-{pid}.sh");
        File.WriteAllText(script, BuildMacScript(archive, bundle, pid));
        return script;
    }

    /// <summary>The macOS swap script body. Split out from the file write so the bundle replacement is
    /// testable: extracting INTO <c>Contents/MacOS</c> instead of replacing the whole <c>.app</c> is
    /// what caused the "restart reopens the old version, re-downloads, loops" bug.</summary>
    internal static string BuildMacScript(string archive, string bundle, int pid)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"downloader-update-{pid}");
        var parent = Path.GetDirectoryName(bundle); // e.g. /Applications
        return
            "#!/bin/bash\n" +
            "trap '' HUP\n" +
            $"while kill -0 {pid} 2>/dev/null; do sleep 0.5; done\n" +
            $"rm -rf \"{tmp}\"; mkdir -p \"{tmp}\"\n" +
            $"tar -xzf \"{archive}\" -C \"{tmp}\"\n" +
            $"if [ -d \"{tmp}/Downloader.app\" ]; then\n" +
            $"  rm -rf \"{bundle}\"\n" +
            $"  mv \"{tmp}/Downloader.app\" \"{parent}/\"\n" +
            $"  xattr -dr com.apple.quarantine \"{bundle}\" 2>/dev/null\n" +
            "fi\n" +
            $"rm -rf \"{tmp}\" \"{archive}\"\n" +
            $"open \"{bundle}\"\n";
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(
        Justification = "Writes the swap script to a temp path; the script BODY it delegates to is covered in UpdateStackTests.")]
    private static string WriteWindowsScript(string archive, string appDir, string exe, int pid)
    {
        var script = Path.Combine(Path.GetTempPath(), $"downloader-update-{pid}.cmd");
        File.WriteAllText(script, BuildWindowsScript(archive, appDir, exe, pid));
        return script;
    }

    /// <summary>The Windows swap script body (#9). Hardened against the reported "downloaded but couldn't
    /// restart/replace" failure: (a) sleeps use `ping` — `timeout /t` dies instantly ("input redirection
    /// is not supported") in the no-console process we spawn, making the PID wait a hot spin; (b) the
    /// extraction RETRIES for up to ~60s until Downloader.exe is actually replaceable — a stale tray-held
    /// instance or an AV scan keeping the exe locked used to make a single extraction attempt fail
    /// silently and fall through to relaunching the OLD build; (c) the old build is only relaunched as a
    /// last resort when every retry failed (better than leaving the user with nothing running).
    ///
    /// <para>Extraction uses the in-box <c>tar.exe</c> (Windows 10 1803+, bsdtar — it reads zip) by its
    /// absolute <c>%SystemRoot%</c> path, NOT PowerShell's <c>Expand-Archive</c> (issue #4): an unsigned
    /// binary spawning PowerShell to overwrite its own executable is the strongest signal a behavioral
    /// antivirus engine can see. The absolute path also removes any PATH-hijacking window.</para></summary>
    internal static string BuildWindowsScript(string archive, string appDir, string exe, int pid) =>
        "@echo off\r\n" +
        ":wait\r\n" +
        $"tasklist /FI \"PID eq {pid}\" | find \"{pid}\" >nul && (ping -n 2 127.0.0.1 >nul & goto wait)\r\n" +
        "set tries=0\r\n" +
        ":extract\r\n" +
        $"\"%SystemRoot%\\System32\\tar.exe\" -x -f \"{archive}\" -C \"{appDir}\" && goto done\r\n" +
        "set /a tries+=1\r\n" +
        "if %tries% lss 60 (ping -n 2 127.0.0.1 >nul & goto extract)\r\n" +
        ":done\r\n" +
        $"del \"{archive}\"\r\n" +
        $"start \"\" \"{exe}\"\r\n";
}
