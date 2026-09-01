using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Downloader.Desktop.Models;
using Microsoft.Win32;

namespace Downloader.Desktop.Services;

/// <summary>
/// Finds which supported browsers are installed on this machine.
///
/// <para><b>The boundary is the point of this class: it reads only whether a browser EXISTS and where its
/// executable is.</b> No profile directory, no cookie store, no saved-credential store, no history, no
/// preferences file is ever opened. Reading browser data is textbook infostealer behaviour and is what
/// made the HLS plugin drop <c>--cookies-from-browser</c> (CLAUDE.md, issue #4); a feature about browsers
/// is exactly where that line would be crossed by accident, so it is guarded by a source scan in
/// <c>NoShellSpawnTests</c>.</para>
///
/// <para>The candidate list is deliberately CURATED rather than "anything that looks like a browser":
/// enumerating the machine invites false positives and edges toward profiling it.</para>
///
/// <para>Every path is failure-tolerant — an unreadable registry key or a missing directory yields no
/// entry, never an exception to the caller.</para>
/// </summary>
public static class BrowserDetector
{
    /// <summary>One supported browser and how to find it on each platform.</summary>
    private sealed record Candidate(
        string Id,
        string Name,
        BrowserFamily Family,
        string WindowsExe,
        string[] UnixNames,
        string MacBundle);

    /// <summary>Test seam: replaces detection wholesale. A test cannot install a browser, and the real
    /// result differs per developer machine, so everything downstream of detection (the dialog's list,
    /// per-family modes, the connected marker) would otherwise be unreachable. The app never sets it.</summary>
    internal static Func<IReadOnlyList<DetectedBrowser>> DetectOverride { get; set; }

    private static readonly Candidate[] Candidates =
    {
        new("chrome",   "Google Chrome", BrowserFamily.Chromium, "chrome.exe",
            new[] { "google-chrome", "google-chrome-stable", "chrome" }, "Google Chrome"),
        new("edge",     "Microsoft Edge", BrowserFamily.Chromium, "msedge.exe",
            new[] { "microsoft-edge", "microsoft-edge-stable", "msedge" }, "Microsoft Edge"),
        new("brave",    "Brave", BrowserFamily.Chromium, "brave.exe",
            new[] { "brave-browser", "brave" }, "Brave Browser"),
        new("vivaldi",  "Vivaldi", BrowserFamily.Chromium, "vivaldi.exe",
            new[] { "vivaldi", "vivaldi-stable" }, "Vivaldi"),
        new("opera",    "Opera", BrowserFamily.Chromium, "opera.exe",
            new[] { "opera" }, "Opera"),
        new("chromium", "Chromium", BrowserFamily.Chromium, "chromium.exe",
            new[] { "chromium", "chromium-browser" }, "Chromium"),
        new("firefox",  "Mozilla Firefox", BrowserFamily.Gecko, "firefox.exe",
            new[] { "firefox", "firefox-esr" }, "Firefox"),
        new("librewolf","LibreWolf", BrowserFamily.Gecko, "librewolf.exe",
            new[] { "librewolf" }, "LibreWolf"),
    };

    /// <summary>Extra directories worth checking beyond <c>PATH</c> on Linux — snap and flatpak both
    /// export launchers outside a default PATH in some shells.</summary>
    private static string[] UnixExtraDirs => new[]
    {
        "/usr/bin", "/usr/local/bin", "/bin", "/opt/bin", "/snap/bin",
        "/var/lib/flatpak/exports/bin",
        Path.Combine(Home, ".local", "share", "flatpak", "exports", "bin"),
        Path.Combine(Home, ".local", "bin"),
    };

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? "";

    /// <summary>
    /// The supported browsers installed on this machine, de-duplicated by id and in a stable order
    /// (Chromium family first, then Gecko, each in the curated order) so the dialog's list does not
    /// reshuffle between opens.
    /// </summary>
    public static IReadOnlyList<DetectedBrowser> Detect()
    {
        if (DetectOverride is { } stub)
        {
            try { return stub() ?? Array.Empty<DetectedBrowser>(); }
            catch { return Array.Empty<DetectedBrowser>(); }
        }

        var found = new List<DetectedBrowser>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in Candidates)
        {
            string exe = null;
            try
            {
                if (OperatingSystem.IsWindows())
                    exe = FindOnWindows(c);
                else if (OperatingSystem.IsMacOS())
                    exe = FindOnMac(c);
                else
                    exe = FindOnUnix(c);
            }
            catch
            {
                // A single unreadable key/directory must not lose the whole list.
            }

            if (string.IsNullOrWhiteSpace(exe) || !seen.Add(c.Id))
                continue;

            found.Add(new DetectedBrowser
            {
                Id = c.Id,
                Name = c.Name,
                Family = c.Family,
                ExecutablePath = exe,
            });
        }

        return found;
    }

    // ---------------- Windows ----------------

    /// <summary>
    /// Registry only, via <see cref="Registry"/> — never by spawning <c>reg.exe</c> (a spawned child
    /// process plus a browser lookup is the shape that got this app quarantined; see issue #4).
    /// Two sources, in order: the Start-menu internet-clients list, then the App Paths table.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static string FindOnWindows(Candidate c)
    {
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            var clients = TryOpen(root, @"SOFTWARE\Clients\StartMenuInternet");
            if (clients == null)
                continue;
            using (clients)
            {
                foreach (var client in SubKeyNames(clients))
                {
                    using var cmd = TryOpen(clients, client + @"\shell\open\command");
                    var raw = cmd?.GetValue(null) as string;
                    var path = UnquoteCommand(raw);
                    if (path != null
                        && string.Equals(Path.GetFileName(path), c.WindowsExe, StringComparison.OrdinalIgnoreCase)
                        && File.Exists(path))
                        return path;
                }
            }
        }

        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var app = TryOpen(root, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + c.WindowsExe);
            var path = UnquoteCommand(app?.GetValue(null) as string);
            if (path != null && File.Exists(path))
                return path;
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static RegistryKey TryOpen(RegistryKey parent, string sub)
    {
        try { return parent?.OpenSubKey(sub); }
        catch { return null; }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> SubKeyNames(RegistryKey key)
    {
        try { return key.GetSubKeyNames(); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>
    /// A registered open command is an executable plus arguments, usually quoted:
    /// <c>"C:\Program Files\Google\Chrome\Application\chrome.exe" -- "%1"</c>. Take the executable.
    /// Pure so it is tested on every platform, not only Windows.
    /// </summary>
    internal static string UnquoteCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;
        var s = command.Trim();
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            return end > 1 ? s[1..end] : null;
        }
        // Unquoted: everything up to the first argument separator. A space inside an unquoted path is
        // unrecoverable here, which is why the quoted form is the one that matters.
        var cut = s.IndexOf(" -", StringComparison.Ordinal);
        if (cut > 0)
            s = s[..cut];
        s = s.Trim();
        return s.Length == 0 ? null : s;
    }

    // ---------------- Linux / other Unix ----------------

    private static string FindOnUnix(Candidate c)
    {
        var dirs = new List<string>();
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        dirs.AddRange(pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        dirs.AddRange(UnixExtraDirs);

        foreach (var dir in dirs)
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            foreach (var name in c.UnixNames)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }

    // ---------------- macOS ----------------

    private static string FindOnMac(Candidate c)
    {
        foreach (var apps in new[] { "/Applications", Path.Combine(Home, "Applications") })
        {
            var bundle = Path.Combine(apps, c.MacBundle + ".app");
            if (!Directory.Exists(bundle))
                continue;
            // The bundle's own binary is not always named after the bundle (Chrome ships
            // "Google Chrome"), so take whatever single executable Contents/MacOS holds.
            var macOs = Path.Combine(bundle, "Contents", "MacOS");
            try
            {
                var inner = Directory.Exists(macOs) ? Directory.GetFiles(macOs) : Array.Empty<string>();
                if (inner.Length > 0)
                    return inner.FirstOrDefault(f => Path.GetFileName(f) == c.MacBundle) ?? inner[0];
            }
            catch
            {
                // fall through to the bundle path — `open` handles a bundle fine
            }
            return bundle;
        }
        return null;
    }
}
