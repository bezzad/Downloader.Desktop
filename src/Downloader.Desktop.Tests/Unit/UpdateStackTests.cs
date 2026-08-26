using System;
using System.Text.Json;
using System.Threading.Tasks;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// The update stack's decision logic: which release counts as newer, which asset belongs to this
/// platform, and the guard clauses that keep <see cref="UpdateFlow"/> from acting at the wrong time.
///
/// Deliberately no network: <see cref="UpdateService.CheckAsync"/> and the actual archive swap are the
/// two parts that cannot run here (the swap replaces the running install), so they stay covered only by
/// their pure pieces — the release-JSON parsing, the version compare and the generated scripts.
/// </summary>
public class UpdateStackTests
{
    // ---- version comparison ------------------------------------------------

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("v2.0.0", "1.9.9", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("v1.9.9", "1.9.9", false)]   // same version is NOT newer
    [InlineData("v1.9.8", "1.9.9", false)]
    [InlineData("v1.10.0", "1.9.0", true)]   // numeric compare, not lexicographic
    [InlineData("v2.6.1", "2.6.0", true)]    // a patch release IS an update
    [InlineData(null, "1.0.0", false)]
    [InlineData("", "1.0.0", false)]
    [InlineData("not-a-version", "1.0.0", false)]
    public void IsNewer_compares_tags_numerically(string? tag, string current, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsNewer(tag, Version.Parse(current)));
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("V1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("  v1.2.3  ", "1.2.3")]
    [InlineData("1.2", "1.2.0")]        // missing parts default to zero
    [InlineData("1", "1.0.0")]
    [InlineData("v2.6.1", "2.6.1")]
    public void Normalize_parses_tag_shapes(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateService.Normalize(tag));
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("vNext")]
    [InlineData("beta")]
    public void Normalize_returns_null_for_junk(string? tag)
    {
        Assert.Null(UpdateService.Normalize(tag));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Normalize_ignores_a_fourth_version_part()
    {
        // The app stamps a build/revision from the UTC build time; only major.minor.patch is compared,
        // otherwise every rebuild would look like a different version.
        Assert.Equal(new Version(1, 2, 3), UpdateService.Normalize("v1.2.3.4444"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void CurrentVersion_is_three_part_and_real()
    {
        var v = UpdateService.CurrentVersion;

        Assert.True(v.Major >= 1, "app version should be at least 1.x");
        Assert.Equal(-1, v.Revision); // built from (major, minor, build) only
        Assert.True(v > new Version(0, 0, 0));
    }

    // ---- asset selection ---------------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void ExpectedAssetName_matches_this_platforms_release_asset()
    {
        var name = UpdateService.ExpectedAssetName();

        Assert.StartsWith("Downloader-", name);

        // Naming must stay in lockstep with release.yml's matrix, or an update silently degrades to
        // "open the release page in a browser".
        if (OperatingSystem.IsWindows())
            Assert.EndsWith(".zip", name);
        else
            Assert.EndsWith(".tar.gz", name);

        if (OperatingSystem.IsLinux())
            Assert.Contains("linux-", name);
        if (OperatingSystem.IsMacOS())
            Assert.Contains("osx-", name);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void FindAsset_picks_this_platforms_archive_out_of_a_release_payload()
    {
        var want = UpdateService.ExpectedAssetName();
        var json = $$"""
        {
          "tag_name": "v9.9.9",
          "html_url": "https://github.com/bezzad/Downloader.Desktop/releases/tag/v9.9.9",
          "assets": [
            { "name": "Downloader-some-other-rid.tar.gz", "browser_download_url": "https://example.invalid/other" },
            { "name": "{{want}}", "browser_download_url": "https://example.invalid/mine" },
            { "name": "plugins-catalog.json", "browser_download_url": "https://example.invalid/catalog" }
          ]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var (url, name) = UpdateService.FindAsset(doc.RootElement);

        Assert.Equal("https://example.invalid/mine", url);
        Assert.Equal(want, name);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void FindAsset_matches_the_name_case_insensitively()
    {
        var want = UpdateService.ExpectedAssetName().ToUpperInvariant();
        var json = $$"""
        { "assets": [ { "name": "{{want}}", "browser_download_url": "https://example.invalid/mine" } ] }
        """;

        using var doc = JsonDocument.Parse(json);
        var (url, _) = UpdateService.FindAsset(doc.RootElement);

        Assert.Equal("https://example.invalid/mine", url);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("""{ "assets": [] }""")]                                              // release with no assets
    [InlineData("""{ }""")]                                                            // no assets property
    [InlineData("""{ "assets": "not-an-array" }""")]                                   // wrong shape
    [InlineData("""{ "assets": [ { "name": "Downloader-nope.tar.gz" } ] }""")]         // no match for us
    [InlineData("""{ "assets": [ { "browser_download_url": "https://x.invalid/a" } ] }""")] // nameless asset
    public void FindAsset_returns_nothing_rather_than_throwing(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var (url, name) = UpdateService.FindAsset(doc.RootElement);

        Assert.Null(url);
        Assert.Null(name);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void FindAsset_ignores_an_asset_that_matches_but_has_no_download_url()
    {
        var want = UpdateService.ExpectedAssetName();
        using var doc = JsonDocument.Parse($$"""{ "assets": [ { "name": "{{want}}" } ] }""");

        var (url, _) = UpdateService.FindAsset(doc.RootElement);

        Assert.Null(url);
    }

    // ---- UpdateFlow guard clauses -----------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Flow_starts_idle_and_is_not_ready()
    {
        UpdateFlow.ResetForTests();

        Assert.Equal(UpdateState.Idle, UpdateFlow.State);
        Assert.False(UpdateFlow.IsReady);
        Assert.Equal(0, UpdateFlow.Progress);
        Assert.Null(UpdateFlow.AvailableTag);
        Assert.Null(UpdateFlow.AvailableReleaseUrl);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Flow_does_nothing_when_no_update_is_staged()
    {
        UpdateFlow.ResetForTests();
        var quits = 0;
        UpdateFlow.RequestQuit = () => quits++;

        // Not Ready: neither of these may act.
        UpdateFlow.ApplyAndRestart();
        UpdateFlow.ApplyPendingOnExit();

        Assert.Equal(0, quits);
        Assert.Equal(UpdateState.Idle, UpdateFlow.State);

        // Cancelling a download that never started must be a silent no-op, not a throw.
        UpdateFlow.CancelDownload();
        UpdateFlow.Dismiss(); // Dismiss only acts on Available
        Assert.Equal(UpdateState.Idle, UpdateFlow.State);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Flow_check_is_skipped_when_updates_are_managed_externally()
    {
        UpdateFlow.ResetForTests();
        var previous = Environment.GetEnvironmentVariable("SNAP");
        try
        {
            // Under snap the Store owns updates — the in-app updater must disable itself, and this
            // is also what keeps the test off the network.
            Environment.SetEnvironmentVariable("SNAP", "/snap/downloader/current");
            Assert.True(UpdateFlow.IsManagedExternally);

            await UpdateFlow.CheckAsync(manual: false);

            Assert.Equal(UpdateState.Idle, UpdateFlow.State);
            Assert.Null(UpdateFlow.AvailableTag);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SNAP", previous);
        }

        Assert.Equal(string.IsNullOrEmpty(previous), !UpdateFlow.IsManagedExternally);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Flow_start_download_no_ops_without_a_pending_update()
    {
        UpdateFlow.ResetForTests();

        await UpdateFlow.StartDownloadAsync();

        Assert.Equal(UpdateState.Idle, UpdateFlow.State);
        Assert.False(UpdateFlow.IsReady);
    }

    // ---- swap scripts ------------------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Unix_swap_script_waits_extracts_and_relaunches_detached()
    {
        var script = UpdateService.BuildUnixScript(
            archive: "/tmp/Downloader-linux-x64.tar.gz", appDir: "/opt/downloader",
            exe: "/opt/downloader/Downloader", pid: 4242);

        // Wait for the old process to actually exit before overwriting its files.
        Assert.Contains("kill -0 4242", script);

        // The swapper must survive the parent's exit, and the NEW app must start in its own session —
        // without that detachment "restart to update" appears to do nothing, because the relaunched
        // app is torn down with the old process group.
        Assert.Contains("trap '' HUP", script);
        Assert.Contains("setsid", script);
        Assert.Contains("nohup", script); // fallback where setsid is unavailable

        Assert.Contains("tar -xzf \"/tmp/Downloader-linux-x64.tar.gz\" -C \"/opt/downloader\"", script);
        Assert.Contains("chmod +x", script); // the extracted apphost must stay executable
        Assert.Contains("rm -f \"/tmp/Downloader-linux-x64.tar.gz\"", script);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Unix_swap_script_uses_unzip_for_a_zip_archive()
    {
        var script = UpdateService.BuildUnixScript("/tmp/Downloader.zip", "/opt/downloader",
            "/opt/downloader/Downloader", 7);

        Assert.Contains("unzip -o", script);
        Assert.DoesNotContain("tar -xzf", script);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Mac_swap_script_replaces_the_whole_app_bundle()
    {
        var script = UpdateService.BuildMacScript(
            archive: "/tmp/Downloader-osx-arm64.tar.gz",
            bundle: "/Applications/Downloader.app", pid: 99);

        Assert.Contains("kill -0 99", script);

        // The whole bundle is replaced. Extracting INTO Contents/MacOS nests a second .app and
        // relaunches the OLD binary, which re-detects the update — the restart loop.
        Assert.Contains("rm -rf \"/Applications/Downloader.app\"", script);
        Assert.Contains("mv ", script);
        Assert.Contains("/Applications/Downloader.app", script);
        Assert.DoesNotContain("Contents/MacOS", script);

        // Gatekeeper would otherwise refuse the freshly downloaded bundle.
        Assert.Contains("xattr -dr com.apple.quarantine", script);
        Assert.Contains("open \"/Applications/Downloader.app\"", script);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void No_swap_script_ever_reaches_for_powershell()
    {
        // Issue #4: an unsigned binary spawning PowerShell to overwrite its own executable is the
        // strongest signal a behavioural antivirus engine can see. Guarded for every platform here,
        // not just Windows.
        foreach (var script in new[]
                 {
                     UpdateService.BuildUnixScript("/tmp/a.tar.gz", "/opt/d", "/opt/d/D", 1),
                     UpdateService.BuildMacScript("/tmp/a.tar.gz", "/Applications/Downloader.app", 1),
                     UpdateService.BuildWindowsScript(@"C:\a.zip", @"C:\d", @"C:\d\D.exe", 1)
                 })
        {
            Assert.DoesNotContain("powershell", script, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Expand-Archive", script, System.StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Apply_downloaded_archive_refuses_a_missing_file()
    {
        var missing = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "downloader-does-not-exist-" + Guid.NewGuid().ToString("N") + ".tar.gz");

        // Must fail cleanly rather than spawning a swap script that would wipe the install with nothing.
        Assert.False(UpdateService.ApplyDownloadedArchive(missing));
    }
}
