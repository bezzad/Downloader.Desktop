using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// <see cref="BrowserDetector"/> — what it finds, and (the part that matters) what it must never touch.
/// The privacy boundary itself is enforced by the source scan in <see cref="NoShellSpawnTests"/>, which
/// bans the profile-path fragments outright; these tests cover the behaviour around it.
/// </summary>
public class BrowserDetectorTests
{
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Detect_never_throws_and_every_entry_is_usable()
    {
        var found = BrowserDetector.Detect();

        Assert.NotNull(found);
        foreach (var b in found)
        {
            Assert.False(string.IsNullOrWhiteSpace(b.Id), "a detected browser must carry an id");
            Assert.False(string.IsNullOrWhiteSpace(b.Name), $"{b.Id} has no display name");
            Assert.False(string.IsNullOrWhiteSpace(b.ExecutablePath), $"{b.Id} has no executable path");
            // Detection reported it as installed, so the path it reported must exist — a file on
            // Windows/Linux, possibly an .app bundle directory on macOS.
            Assert.True(File.Exists(b.ExecutablePath) || Directory.Exists(b.ExecutablePath),
                $"{b.Id} reported a path that does not exist: {b.ExecutablePath}");
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Detect_returns_each_browser_at_most_once()
    {
        var ids = BrowserDetector.Detect().Select(b => b.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Detect_lists_chromium_browsers_before_gecko_ones()
    {
        var families = BrowserDetector.Detect().Select(b => b.Family).ToList();
        var firstGecko = families.IndexOf(BrowserFamily.Gecko);
        if (firstGecko < 0)
            return; // no Gecko browser on this machine — nothing to order

        Assert.DoesNotContain(BrowserFamily.Chromium, families.Skip(firstGecko));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Detect_is_stable_across_calls()
    {
        var a = BrowserDetector.Detect().Select(b => b.Id).ToList();
        var b2 = BrowserDetector.Detect().Select(b => b.Id).ToList();
        Assert.Equal(a, b2);
    }

    /// <summary>The override is what makes everything downstream of detection testable — a test cannot
    /// install a browser, and the real result differs per machine.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Detect_can_be_stubbed_and_a_throwing_stub_is_not_fatal()
    {
        try
        {
            BrowserDetector.DetectOverride = () => new List<DetectedBrowser>
            {
                new() { Id = "chrome", Name = "Google Chrome", Family = BrowserFamily.Chromium, ExecutablePath = "/x/chrome" },
            };
            Assert.Equal("chrome", Assert.Single(BrowserDetector.Detect()).Id);

            BrowserDetector.DetectOverride = () => throw new InvalidOperationException("boom");
            Assert.Empty(BrowserDetector.Detect());

            BrowserDetector.DetectOverride = () => null;
            Assert.Empty(BrowserDetector.Detect());
        }
        finally
        {
            // Process-wide: leaking it silently changes a LATER test, not this one.
            BrowserDetector.DetectOverride = null;
        }
    }

    // ---- UnquoteCommand: the Windows registry value shape, testable on every platform ----

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\" -- \"%1\"",
                "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe")]
    [InlineData("\"C:\\Program Files\\Mozilla Firefox\\firefox.exe\"",
                "C:\\Program Files\\Mozilla Firefox\\firefox.exe")]
    [InlineData("C:\\browsers\\opera.exe -- \"%1\"", "C:\\browsers\\opera.exe")]
    [InlineData("C:\\browsers\\opera.exe", "C:\\browsers\\opera.exe")]
    public void UnquoteCommand_takes_the_executable(string command, string expected)
        => Assert.Equal(expected, BrowserDetector.UnquoteCommand(command));

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"")]
    [InlineData("\"\"")]
    public void UnquoteCommand_returns_null_for_nothing_usable(string? command)
        => Assert.Null(BrowserDetector.UnquoteCommand(command));

    // ---------------- every supported browser, on every platform ----------------

    /// <summary>
    /// The list the extension dialog shows. It must contain EVERY supported browser whatever this machine
    /// has, because detection can only prove presence: a snap-confined app sees the base snap's
    /// <c>/usr/bin</c>, so a perfectly ordinary Chrome install is invisible to it. Hiding the undetected
    /// ones hid the browser the user was actually running.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void All_lists_every_supported_browser_whatever_is_installed_here()
    {
        var all = BrowserDetector.All();

        Assert.Equal(BrowserDetector.Supported.Count, all.Count);
        Assert.Equal(BrowserDetector.Supported.Select(c => c.Id), all.Select(b => b.Id));
        Assert.All(all, b =>
        {
            Assert.False(string.IsNullOrWhiteSpace(b.Id));
            Assert.False(string.IsNullOrWhiteSpace(b.Name));
            // The flag and the path can never disagree.
            Assert.Equal(b.IsInstalled, !string.IsNullOrWhiteSpace(b.ExecutablePath));
        });
        // Both families are represented, or one whole family could never be offered.
        Assert.Contains(BrowserFamily.Chromium, all.Select(b => b.Family));
        Assert.Contains(BrowserFamily.Gecko, all.Select(b => b.Family));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Detect_is_All_minus_what_was_not_found()
    {
        Assert.Equal(BrowserDetector.All().Where(b => b.IsInstalled).Select(b => b.Id),
                     BrowserDetector.Detect().Select(b => b.Id));
    }

    /// <summary>
    /// A lookup on every platform for every browser — the author's point that "it surely breaks somewhere
    /// else". A missing Windows exe or macOS bundle name is a browser that can never be found on that OS,
    /// and neither can be reproduced from this box any other way.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Every_browser_has_a_lookup_on_windows_linux_and_macos()
    {
        Assert.NotEmpty(BrowserDetector.Supported);
        Assert.All(BrowserDetector.Supported, c =>
        {
            Assert.EndsWith(".exe", c.WindowsExe, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(c.MacBundle));
            Assert.NotEmpty(c.UnixNames);
            Assert.All(c.UnixNames, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        });
    }

    // ---------------- Linux: the layouts a real machine actually uses ----------------

    /// <summary>
    /// The reported bug: Chrome was installed and the dialog did not list it. A <c>.deb</c> Chrome lives
    /// in <c>/opt/google/chrome</c> and its <c>/usr/bin</c> entry is only a symlink, so a filesystem view
    /// without the host's <c>/usr/bin</c> — exactly what a strictly confined snap has — finds nothing
    /// unless the vendor directory is searched too.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_deb_installed_chrome_is_found_in_its_vendor_directory()
    {
        var chrome = BrowserDetector.Supported.Single(c => c.Id == "chrome");
        var onDisk = new HashSet<string>(StringComparer.Ordinal) { "/opt/google/chrome/google-chrome" };

        var found = BrowserDetector.FindUnixExecutable(chrome.UnixNames,
            BrowserDetector.UnixSearchDirs(pathVar: "/home/me/bin"), onDisk.Contains);

        Assert.Equal("/opt/google/chrome/google-chrome", found);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("edge", "/opt/microsoft/msedge/msedge")]
    [InlineData("brave", "/opt/brave.com/brave/brave-browser")]
    [InlineData("vivaldi", "/opt/vivaldi/vivaldi")]
    [InlineData("opera", "/opt/opera/opera")]
    [InlineData("firefox", "/usr/lib/firefox/firefox")]
    public void Each_vendor_install_layout_is_searched(string id, string path)
    {
        var c = BrowserDetector.Supported.Single(x => x.Id == id);
        var found = BrowserDetector.FindUnixExecutable(c.UnixNames,
            BrowserDetector.UnixSearchDirs(pathVar: ""), p => p == path);
        Assert.Equal(path, found);
    }

    /// <summary>
    /// Under strict snap confinement the host's filesystem is reachable only through
    /// <c>/var/lib/snapd/hostfs</c> — the app's own <c>/usr/bin</c> is the base snap's. This is the case
    /// the reporter hit, and it is the only way a confined app can see a normally installed browser.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_browser_only_visible_through_the_snap_host_prefix_is_still_found()
    {
        var chrome = BrowserDetector.Supported.Single(c => c.Id == "chrome");
        const string hostPath = "/var/lib/snapd/hostfs/usr/bin/google-chrome";

        var found = BrowserDetector.FindUnixExecutable(chrome.UnixNames,
            BrowserDetector.UnixSearchDirs(pathVar: "/usr/bin"), p => p == hostPath);

        Assert.Equal(hostPath, found);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_app_s_own_view_is_preferred_over_the_snap_host_view()
    {
        // Both exist: the un-prefixed path is the one the app can actually execute.
        var chrome = BrowserDetector.Supported.Single(c => c.Id == "chrome");
        var found = BrowserDetector.FindUnixExecutable(chrome.UnixNames,
            BrowserDetector.UnixSearchDirs(pathVar: "/usr/bin"),
            p => p == "/usr/bin/google-chrome" || p == "/var/lib/snapd/hostfs/usr/bin/google-chrome");

        Assert.Equal("/usr/bin/google-chrome", found);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_denied_directory_reads_as_not_found_instead_of_throwing()
    {
        // AppArmor denies the host prefix on most machines; detection must survive it.
        var found = BrowserDetector.FindUnixExecutable(new[] { "google-chrome" },
            BrowserDetector.UnixSearchDirs(pathVar: "/usr/bin"),
            _ => throw new UnauthorizedAccessException("denied"));

        Assert.Null(found);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_search_never_double_prefixes_the_host_view()
    {
        var dirs = BrowserDetector.UnixSearchDirs(pathVar: "/var/lib/snapd/hostfs/usr/bin");

        Assert.DoesNotContain(dirs, d => d.Contains("hostfs/var/lib/snapd/hostfs", StringComparison.Ordinal));
        Assert.Contains("/var/lib/snapd/hostfs/usr/bin", dirs);
    }

    // ---------------- macOS ----------------

    /// <summary>
    /// macOS was never covered — "I did not test on a Mac, your code surely breaks everywhere". A bundle
    /// binary is not always named after the bundle (Chrome's is "Google Chrome"), and the fallback is the
    /// bundle path itself, which `open` handles.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_mac_bundle_resolves_to_the_binary_named_after_it()
    {
        var found = BrowserDetector.FindMacBundle("Google Chrome", new[] { "/Applications" },
            dirExists: d => d is "/Applications/Google Chrome.app"
                            or "/Applications/Google Chrome.app/Contents/MacOS",
            listFiles: _ => new[]
            {
                "/Applications/Google Chrome.app/Contents/MacOS/chrome_crashpad_handler",
                "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
            });

        Assert.Equal("/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", found);
    }

    /// <summary>
    /// A browser installed for one user only lives in THEIR Applications folder, so the second root has
    /// to be searched as well. Fixed paths, not the runner's own home, so this asserts the same thing on
    /// every OS.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_mac_bundle_in_the_users_own_Applications_folder_is_found_too()
    {
        const string bundle = "/Users/me/Applications/Firefox.app";

        var found = BrowserDetector.FindMacBundle("Firefox",
            new[] { "/Applications", "/Users/me/Applications" },
            dirExists: d => d == bundle, listFiles: _ => Array.Empty<string>());

        Assert.Equal(bundle, found);   // no readable Contents/MacOS → the bundle itself, which `open` takes
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Both_mac_application_roots_are_searched()
    {
        Assert.Equal(2, BrowserDetector.MacAppDirs.Count);
        Assert.Equal("/Applications", BrowserDetector.MacAppDirs[0]);
        Assert.EndsWith("/Applications", BrowserDetector.MacAppDirs[1], StringComparison.Ordinal);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_missing_mac_bundle_is_null_not_an_exception()
    {
        Assert.Null(BrowserDetector.FindMacBundle("Nope", new[] { "/Applications" },
            dirExists: _ => throw new IOException("boom"), listFiles: _ => Array.Empty<string>()));
        Assert.Null(BrowserDetector.FindMacBundle(null, new[] { "/Applications" }, _ => true, _ => null));
    }
}
