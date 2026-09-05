using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.Tests.Plugins.Hls;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// <see cref="ExtensionInstallService"/> — the verify-then-unpack core.
///
/// The checksum gate and the archive-path check are the whole safety story of this feature: the app
/// downloads files from the internet and puts them where a browser will read them. Every one of these
/// tests is about what happens when that download is NOT what it claims to be, plus the stable-path
/// requirement (a browser identifies a manually loaded extension by its folder path, so a moving path
/// silently resets the extension's identity and settings).
/// </summary>
public class ExtensionInstallServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dldesktop-ext-" + Guid.NewGuid().ToString("N")[..8]);

    public ExtensionInstallServiceTests() => ExtensionInstallService.InstallRootOverride = _root;

    public void Dispose()
    {
        // Process-wide seam: leaking it would silently change a LATER test, not this one.
        ExtensionInstallService.InstallRootOverride = null;
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    // ---- fixtures ----

    private static byte[] BuildZip(params (string Name, string Content)[] files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                var e = zip.CreateEntry(name);
                using var w = new StreamWriter(e.Open());
                w.Write(content);
            }
        }
        return ms.ToArray();
    }

    private static string Sha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static ExtensionCatalogEntry Entry(LoopbackServer server, string sha, string id = "chrome", string version = "1.7.0")
        => new()
        {
            Id = id,
            Family = "chromium",
            Name = "Chrome",
            Version = version,
            AssetName = "ext.zip",
            AssetUrl = server.Url("ext.zip"),
            Sha256 = sha,
            MinAppVersion = "1.0.0",
        };

    private static readonly (string, string)[] GoodFiles =
    {
        ("manifest.json", "{\"manifest_version\":3,\"version\":\"1.7.0\"}"),
        ("common.js", "// code"),
        ("icons/icon16.png", "png"),
    };

    // ---- the happy path ----

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_verified_build_is_unpacked_and_recorded()
    {
        var zip = BuildZip(GoodFiles);
        using var server = new LoopbackServer();
        server.MapBytes("/ext.zip", zip, "application/zip");

        var result = await ExtensionInstallService.InstallAsync(
            Entry(server, Sha256(zip)), null, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Equal(ExtensionInstallService.TargetPath("chrome"), result.Path);
        Assert.True(File.Exists(Path.Combine(result.Path, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(result.Path, "icons", "icon16.png")));

        var installed = ExtensionInstallService.ReadInstalled("chrome");
        Assert.NotNull(installed);
        Assert.Equal("1.7.0", installed.Version);
        Assert.Equal("chrome", installed.Target);
    }

    /// <summary>
    /// The seam the dialog reads BOTH facts through. It exists because they used to come from two places:
    /// the version from a stubbable call and the folder from a real static one, so a test could stub the
    /// version and still have the dialog print the developer's own app-data path. Stubbing the seam in the
    /// view-model tests cannot prove the seam itself is right, so this exercises the real implementation.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_installed_copy_reports_the_folder_and_the_version_together()
    {
        var zip = BuildZip(GoodFiles);
        using var server = new LoopbackServer();
        server.MapBytes("/ext.zip", zip, "application/zip");

        var result = await ExtensionInstallService.InstallAsync(Entry(server, Sha256(zip), version: "1.12.0"),
            null, TestContext.Current.CancellationToken);
        Assert.True(result.Success, result.Error);

        var copy = ExtensionInstallService.ReadInstalledCopy("chrome");
        Assert.NotNull(copy);
        // The SAME folder the install actually wrote to — not a recomputed guess from another root.
        Assert.Equal(result.Path, copy.Path);
        Assert.Equal("1.12.0", copy.Version);
        Assert.StartsWith(_root, copy.Path);
    }

    /// <summary>Nothing installed must be null, not an empty-versioned copy: the dialog shows the
    /// "Files installed: v…" line off this, so a non-null placeholder would claim an install that
    /// is not there.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void With_nothing_unpacked_there_is_no_installed_copy()
    {
        Assert.Null(ExtensionInstallService.ReadInstalledCopy("chrome"));
        Assert.Null(ExtensionInstallService.ReadInstalledCopy("firefox"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Reporting_progress_reaches_one_hundred_percent()
    {
        var zip = BuildZip(GoodFiles);
        using var server = new LoopbackServer();
        server.MapBytes("/ext.zip", zip, "application/zip");
        // SyncProgress, not Progress<double>: the latter POSTS to whatever SynchronizationContext an
        // earlier [AvaloniaFact] left on this thread, nothing pumps it, and the assertion then sees an
        // empty list — a real macOS CI failure that had nothing to do with the code under test.
        var seen = new SyncProgress<double>();

        await ExtensionInstallService.InstallAsync(Entry(server, Sha256(zip)),
            seen, TestContext.Current.CancellationToken);

        // Progress is what the dialog's bar binds to; never reaching 1.0 leaves it visibly stuck.
        Assert.Contains(1.0, seen.Reports);
    }

    /// <summary>The stable-path rule: a browser keys a manually loaded extension on this folder.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Installing_twice_lands_on_the_same_path()
    {
        var zip = BuildZip(GoodFiles);
        using var server = new LoopbackServer();
        server.MapBytes("/ext.zip", zip, "application/zip");

        var first = await ExtensionInstallService.InstallAsync(Entry(server, Sha256(zip), version: "1.7.0"),
            null, TestContext.Current.CancellationToken);
        var second = await ExtensionInstallService.InstallAsync(Entry(server, Sha256(zip), version: "1.8.0"),
            null, TestContext.Current.CancellationToken);

        Assert.True(first.Success && second.Success);
        Assert.Equal(first.Path, second.Path);
        Assert.Equal("1.8.0", ExtensionInstallService.ReadInstalled("chrome").Version);
        // The swap replaced the folder rather than merging into it, and left no staging leftover.
        Assert.False(Directory.Exists(second.Path + ".new"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_update_replaces_files_rather_than_merging_with_the_old_build()
    {
        using var server = new LoopbackServer();
        var oldZip = BuildZip(("manifest.json", "old"), ("gone.js", "removed next time"));
        server.MapBytes("/ext.zip", oldZip, "application/zip");
        await ExtensionInstallService.InstallAsync(Entry(server, Sha256(oldZip)), null, TestContext.Current.CancellationToken);

        using var server2 = new LoopbackServer();
        var newZip = BuildZip(("manifest.json", "new"));
        server2.MapBytes("/ext.zip", newZip, "application/zip");
        var result = await ExtensionInstallService.InstallAsync(Entry(server2, Sha256(newZip)), null, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        // A file dropped from the extension must actually disappear — a browser loads whatever is in the
        // folder, so a stale leftover would keep running.
        Assert.False(File.Exists(Path.Combine(result.Path, "gone.js")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(result.Path, "manifest.json")));
    }

    // ---- the checksum gate ----

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_build_that_fails_its_checksum_installs_nothing()
    {
        var zip = BuildZip(GoodFiles);
        using var server = new LoopbackServer();
        server.MapBytes("/ext.zip", zip, "application/zip");

        var result = await ExtensionInstallService.InstallAsync(
            Entry(server, sha: new string('a', 64)), null, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("verified", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(ExtensionInstallService.TargetPath("chrome")));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_failed_checksum_leaves_an_existing_install_untouched()
    {
        var good = BuildZip(("manifest.json", "the good build"));
        using var server = new LoopbackServer();
        server.MapBytes("/ext.zip", good, "application/zip");
        var first = await ExtensionInstallService.InstallAsync(Entry(server, Sha256(good)), null, TestContext.Current.CancellationToken);
        Assert.True(first.Success, first.Error);

        using var bad = new LoopbackServer();
        bad.MapBytes("/ext.zip", BuildZip(("manifest.json", "tampered")), "application/zip");
        var second = await ExtensionInstallService.InstallAsync(Entry(bad, sha: new string('b', 64)), null, TestContext.Current.CancellationToken);

        Assert.False(second.Success);
        // Punishing a working install for a bad download would be the worst outcome of all: the user
        // ends up with no extension where they previously had one.
        Assert.Equal("the good build", File.ReadAllText(Path.Combine(first.Path, "manifest.json")));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_build_with_no_checksum_is_refused()
    {
        var zip = BuildZip(GoodFiles);
        using var server = new LoopbackServer();
        server.MapBytes("/ext.zip", zip, "application/zip");

        var result = await ExtensionInstallService.InstallAsync(Entry(server, sha: ""), null, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.False(Directory.Exists(ExtensionInstallService.TargetPath("chrome")));
    }

    // ---- untrusted archive contents ----

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("../escape.txt")]
    [InlineData("nested/../../escape.txt")]
    [InlineData("/etc/cron.d/evil")]
    public async Task An_archive_entry_that_escapes_the_destination_installs_nothing(string entryName)
    {
        var zip = BuildZip(("manifest.json", "{}"), (entryName, "pwned"));
        using var server = new LoopbackServer();
        server.MapBytes("/ext.zip", zip, "application/zip");

        var result = await ExtensionInstallService.InstallAsync(Entry(server, Sha256(zip)), null, TestContext.Current.CancellationToken);

        // The checksum only proves the file is the one the catalog named — it says nothing about what is
        // inside, so the archive stays untrusted input.
        Assert.False(result.Success);
        Assert.Contains("unexpected file path", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(ExtensionInstallService.TargetPath("chrome")));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void FindUnsafeEntry_passes_an_ordinary_extension_zip()
    {
        var path = Path.Combine(_root, "ok.zip");
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(path, BuildZip(GoodFiles));

        Assert.Null(ExtensionInstallService.FindUnsafeEntry(path, out var err));
        Assert.Null(err);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void FindUnsafeEntry_reports_a_read_error_rather_than_throwing()
    {
        var path = Path.Combine(_root, "not-a-zip.zip");
        Directory.CreateDirectory(_root);
        File.WriteAllText(path, "this is not a zip file");

        Assert.Null(ExtensionInstallService.FindUnsafeEntry(path, out var err));
        Assert.NotNull(err);
    }

    // ---- failure paths that must not throw into the dialog ----

    [Fact(Timeout = TestTimeouts.SlowMs)]
    public async Task An_unreachable_asset_reports_a_failure_rather_than_throwing()
    {
        var result = await ExtensionInstallService.InstallAsync(new ExtensionCatalogEntry
        {
            Id = "chrome",
            Version = "1.7.0",
            AssetName = "ext.zip",
            AssetUrl = "http://10.255.255.1/ext.zip",  // the repo's unreachable IP, never a .invalid host
            Sha256 = new string('c', 64),
        }, null, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_asset_the_release_answers_with_an_error_reports_a_failure()
    {
        using var server = new LoopbackServer();
        server.MapStatus("/ext.zip", 404);

        var result = await ExtensionInstallService.InstallAsync(Entry(server, new string('d', 64)),
            null, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.False(Directory.Exists(ExtensionInstallService.TargetPath("chrome")));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Nothing_to_install_is_a_failure_not_a_crash()
    {
        Assert.False((await ExtensionInstallService.InstallAsync(null, null, TestContext.Current.CancellationToken)).Success);
        Assert.False((await ExtensionInstallService.InstallAsync(
            new ExtensionCatalogEntry { Id = "chrome" }, null, TestContext.Current.CancellationToken)).Success);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_cancelled_install_leaves_a_previous_install_intact()
    {
        var good = BuildZip(("manifest.json", "keep me"));
        using var server = new LoopbackServer();
        server.MapBytes("/ext.zip", good, "application/zip");
        var first = await ExtensionInstallService.InstallAsync(Entry(server, Sha256(good)), null, TestContext.Current.CancellationToken);
        Assert.True(first.Success, first.Error);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var second = await ExtensionInstallService.InstallAsync(Entry(server, Sha256(good)), null, cts.Token);

        Assert.False(second.Success);
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(first.Path, "manifest.json")));
    }

    // ---- the marker file is a convenience, never a source of truth ----

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void ReadInstalled_returns_null_when_nothing_is_installed()
        => Assert.Null(ExtensionInstallService.ReadInstalled("chrome"));

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void ReadInstalled_tolerates_a_corrupt_marker()
    {
        var target = ExtensionInstallService.TargetPath("chrome");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "installed.json"), "{ not json");

        Assert.Null(ExtensionInstallService.ReadInstalled("chrome"));
    }

    // ---- the write boundary ----

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Every_target_path_stays_under_the_install_root()
    {
        // Nothing this service writes may land in a browser profile, extension directory or policy
        // location — the path-fragment ban in NoShellSpawnTests is the other half of this rule.
        foreach (var id in new[] { "chrome", "firefox", "../escape", "/etc/passwd", "..\\..\\windows" })
        {
            var path = Path.GetFullPath(ExtensionInstallService.TargetPath(id));
            Assert.StartsWith(Path.GetFullPath(ExtensionInstallService.InstallRoot) + Path.DirectorySeparatorChar, path);
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_default_install_root_sits_beside_the_plugins_folder()
    {
        ExtensionInstallService.InstallRootOverride = null;
        try
        {
            var root = ExtensionInstallService.InstallRoot;
            Assert.Equal("extension", Path.GetFileName(root));
            Assert.Equal("Downloader", Path.GetFileName(Path.GetDirectoryName(root)));
        }
        finally
        {
            ExtensionInstallService.InstallRootOverride = _root;
        }
    }
}
