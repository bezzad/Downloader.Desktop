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
}
