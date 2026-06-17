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

    private static (string url, string name) FindAsset(JsonElement root)
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

            return RunDetached("/bin/bash", WriteUnixScript(archivePath, appDir, exe, pid));
        }
        catch
        {
            return false;
        }
    }

    private static bool RunDetached(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true };
        return Process.Start(psi) != null;
    }

    private static string WriteUnixScript(string archive, string appDir, string exe, int pid)
    {
        var script = Path.Combine(Path.GetTempPath(), $"downloader-update-{pid}.sh");
        var extract = archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? $"unzip -o \"{archive}\" -d \"{appDir}\""
            : $"tar -xzf \"{archive}\" -C \"{appDir}\"";
        File.WriteAllText(script,
            "#!/bin/bash\n" +
            $"while kill -0 {pid} 2>/dev/null; do sleep 0.5; done\n" +
            $"{extract}\n" +
            $"chmod +x \"{exe}\" 2>/dev/null\n" +
            $"rm -f \"{archive}\"\n" +
            $"\"{exe}\" &\n");
        return script;
    }

    private static string WriteWindowsScript(string archive, string appDir, string exe, int pid)
    {
        var script = Path.Combine(Path.GetTempPath(), $"downloader-update-{pid}.cmd");
        File.WriteAllText(script,
            "@echo off\r\n" +
            $":wait\r\n" +
            $"tasklist /FI \"PID eq {pid}\" | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
            $"powershell -NoProfile -Command \"Expand-Archive -Force -LiteralPath '{archive}' -DestinationPath '{appDir}'\"\r\n" +
            $"del \"{archive}\"\r\n" +
            $"start \"\" \"{exe}\"\r\n");
        return script;
    }
}
