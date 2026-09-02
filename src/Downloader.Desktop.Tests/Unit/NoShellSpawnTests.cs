using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// <b>Guardrail for issue #4 — never spawn a shell, and never look like malware.</b>
///
/// <para>Bitdefender's Advanced Threat Defense blocked and quarantined this app on a clean Windows 11
/// machine. Nothing it found was malicious: the app shelled out to PowerShell to post a toast
/// notification, to create a Start-menu shortcut, and to unzip its own update. But
/// <c>Downloader.exe (unsigned) → powershell.exe → conhost.exe</c> is the exact parent→child chain
/// behavioral engines score, and combined with a Start-menu write, a Run-key write and a self-replacing
/// executable it crossed the threshold. Every one of those actions had a direct, in-process API
/// alternative — see <c>WindowsNotifier</c>, <c>StartMenuShortcut</c>, <c>StartupService</c>,
/// <c>UpdateService</c>.</para>
///
/// <para>This test scans the SHIPPING source (app + plugins) and fails if any of it comes back. It is a
/// text scan on purpose: it catches the pattern in a helper, a comment, or a generated script body —
/// anywhere it could reach a user's machine. If you have a genuinely unavoidable case, add it to
/// <see cref="AllowedExactly"/> with the reason, so the exception is reviewed rather than silent.</para>
/// </summary>
public class NoShellSpawnTests
{
    /// <summary>Patterns that must not appear anywhere in shipping source, with the reason and the
    /// in-process alternative to use instead.</summary>
    private static readonly (string Pattern, string Why)[] Banned =
    {
        (@"powershell", "Spawning PowerShell is THE behavioral-AV trigger (issue #4). Call the API in-process: shell/Win32 P/Invoke, Microsoft.Win32.Registry, System.IO.Compression."),
        (@"pwsh", "Same as powershell — no shell spawns."),
        (@"Expand-Archive", "Use System.IO.Compression.ZipFile in-process, or the in-box tar.exe by absolute path."),
        (@"WScript\.Shell", "Script-host COM reads as malware tooling. Use IShellLink directly (see StartMenuShortcut)."),
        (@"-EncodedCommand", "Base64-encoded command lines are scored as obfuscation, full stop."),
        (@"cmd(\.exe)?\s+/c", "Don't route work through the command interpreter; call the API."),
        (@"--cookies-from-browser", "Reading browser cookie stores is textbook infostealer behavior (removed with yt-dlp in HLS 2.0.0)."),
        (@"reg(\.exe)?""?\s*\)?\s*\{?\s*(RedirectStandardOutput|UseShellExecute)", "Write the registry in-process via Microsoft.Win32.Registry, not by spawning reg.exe."),

        // --- Browser data. Same family as --cookies-from-browser above: reading a browser's profile is
        // what an infostealer does, and BrowserDetector exists to look at browsers, so this is exactly
        // where the line gets crossed by accident. Detection reads existence and executable path only.
        (@"Login Data", "Chromium's saved-password store. Browser detection reads existence and executable path only — never a profile."),
        (@"Local State", "Chromium's profile state (holds the cookie-encryption key). Never read a browser profile."),
        (@"Web Data", "Chromium's autofill store. Never read a browser profile."),
        (@"cookies\.sqlite", "Firefox's cookie store. Never read a browser profile."),
        (@"places\.sqlite", "Firefox's history store. Never read a browser profile."),
        (@"profiles\.ini", "Firefox's profile index — reading it is the first step of profile access. Detection needs only the executable."),

        // --- Browser policy / external-install. The ONLY thing OS elevation would buy is a write here,
        // and it is the browser-hijacker signature — scored HIGHER when elevated, not lower. Installing
        // the extension into a browser is the user's action, performed in the browser. See the
        // install-browser-extension change for why this is settled.
        (@"ExtensionInstallForcelist", "Force-installing an extension via enterprise policy is the browser-hijacker signature. The user installs it in their browser."),
        (@"External Extensions", "The browser external-install hook. Don't register extensions behind the user's back."),
        (@"policies\.json", "Firefox enterprise policy. Don't force-install; offer the store listing or the manual steps."),
    };

    /// <summary>Deliberate, reviewed exceptions: exact "relative/path.cs::pattern" keys. Empty by design —
    /// every previous use had an in-process alternative. Add an entry only with a written reason.</summary>
    private static readonly HashSet<string> AllowedExactly = new(StringComparer.OrdinalIgnoreCase);

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Shipping_source_never_spawns_a_shell_or_reads_browser_data()
    {
        var root = FindRepoDir("src");
        var violations = new List<string>();

        foreach (var file in ShippingSources(root))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            // Comments are stripped, string literals are NOT: the ban is on doing the thing, not on
            // explaining why we don't (those explanations are the point). A banned pattern inside a
            // string literal is real — that's how the update script body reaches a user's machine.
            var text = StripComments(File.ReadAllText(file));

            foreach (var (pattern, why) in Banned)
            {
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (!match.Success)
                    continue;
                if (AllowedExactly.Contains($"{relative}::{pattern}"))
                    continue;

                var line = text.Take(match.Index).Count(c => c == '\n') + 1;
                violations.Add($"{relative}:{line}  found \"{match.Value}\"\n      → {why}");
            }
        }

        Assert.True(violations.Count == 0,
            "Shipping source must never spawn a shell or touch browser data (issue #4 — Bitdefender ATC4 "
            + "blocked the app for exactly this). Offenders:\n  " + string.Join("\n  ", violations));
    }

    /// <summary>Blanks out `//`, `///` and `/* … */` comments, leaving string literals and line numbering
    /// intact (comment characters become spaces, newlines survive). String-aware so a `//` inside a URL
    /// literal doesn't swallow the rest of the line.</summary>
    internal static string StripComments(string source)
    {
        var chars = source.ToCharArray();
        var i = 0;
        while (i < chars.Length)
        {
            var c = chars[i];

            // Verbatim string: @"…", where "" is an escaped quote.
            if (c == '@' && i + 1 < chars.Length && chars[i + 1] == '"')
            {
                i += 2;
                while (i < chars.Length)
                {
                    if (chars[i] == '"')
                    {
                        if (i + 1 < chars.Length && chars[i + 1] == '"') { i += 2; continue; }
                        i++;
                        break;
                    }
                    i++;
                }
                continue;
            }

            // Regular string or char literal, with backslash escapes.
            if (c is '"' or '\'')
            {
                var quote = c;
                i++;
                while (i < chars.Length && chars[i] != quote)
                    i += chars[i] == '\\' ? 2 : 1;
                i++;
                continue;
            }

            if (c == '/' && i + 1 < chars.Length && chars[i + 1] == '/')
            {
                while (i < chars.Length && chars[i] != '\n') chars[i++] = ' ';
                continue;
            }

            if (c == '/' && i + 1 < chars.Length && chars[i + 1] == '*')
            {
                while (i < chars.Length && !(chars[i] == '*' && i + 1 < chars.Length && chars[i + 1] == '/'))
                {
                    if (chars[i] != '\n') chars[i] = ' ';
                    i++;
                }
                if (i < chars.Length) { chars[i++] = ' '; }
                if (i < chars.Length) { chars[i++] = ' '; }
                continue;
            }

            i++;
        }

        return new string(chars);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Comment_stripper_keeps_literals_and_line_numbers()
    {
        var stripped = StripComments("// powershell here\nvar s = \"powershell there\"; // and here\n");

        Assert.DoesNotContain("powershell here", stripped);
        Assert.DoesNotContain("and here", stripped);
        Assert.Contains("\"powershell there\"", stripped);          // literals survive — they ship
        Assert.Equal(3, stripped.Split('\n').Length);               // line numbering preserved
        Assert.Contains("var s =", StripComments("var url = \"http://x/y\"; var s = 1;"));
    }

    /// <summary>Sanity check on the scanner itself: a guardrail that can't fail is not a guardrail.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Scanner_actually_matches_the_patterns_it_bans()
    {
        Assert.Contains(Banned, b => Regex.IsMatch(
            "var psi = new ProcessStartInfo(\"powershell.exe\", \"-NoProfile\");", b.Pattern, RegexOptions.IgnoreCase));
        Assert.Contains(Banned, b => Regex.IsMatch(
            "Expand-Archive -Force -LiteralPath 'x.zip'", b.Pattern, RegexOptions.IgnoreCase));
        Assert.Contains(Banned, b => Regex.IsMatch(
            "yt-dlp --cookies-from-browser chrome", b.Pattern, RegexOptions.IgnoreCase));
        Assert.Contains(Banned, b => Regex.IsMatch(
            @"Path.Combine(profile, ""Login Data"")", b.Pattern, RegexOptions.IgnoreCase));
        Assert.Contains(Banned, b => Regex.IsMatch(
            @"key.SetValue(""ExtensionInstallForcelist"", id)", b.Pattern, RegexOptions.IgnoreCase));
        Assert.Contains(Banned, b => Regex.IsMatch(
            @"var p = ""/etc/firefox/policies/policies.json"";", b.Pattern, RegexOptions.IgnoreCase));
    }

    /// <summary>The app and every plugin — i.e. everything that reaches a user's machine. The test project
    /// itself is excluded: this file necessarily names the patterns it bans.</summary>
    private static IEnumerable<string> ShippingSources(string srcRoot)
    {
        var projects = new[]
        {
            Path.Combine(srcRoot, "Downloader.Desktop"),
            Path.Combine(srcRoot, "Downloader.Desktop.Plugins"),
            Path.Combine(srcRoot, "Downloader.Desktop.Plugins.Abstractions"),
        };

        foreach (var project in projects.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
            {
                var path = file.Replace('\\', '/');
                if (path.Contains("/bin/") || path.Contains("/obj/"))
                    continue;
                yield return file;
            }
        }
    }

    private static string FindRepoDir(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException($"Could not locate '{relative}' above {AppContext.BaseDirectory}");
    }
}
