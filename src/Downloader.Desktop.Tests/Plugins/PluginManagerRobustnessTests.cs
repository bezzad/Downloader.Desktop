using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins;

/// <summary>
/// What the plugin loader does with input it should refuse.
///
/// This is the app's only code path that loads and runs third-party assemblies, so its guards are the
/// security boundary as much as a robustness measure: a zip whose hash does not match must not be
/// extracted at all (not extracted-then-checked), and a plugin that registers nothing, throws on
/// initialise, or duplicates an existing id must be dropped without disturbing what is already
/// loaded. Every one of these paths is a branch that the happy-path tests never take.
///
/// Installs go to a temp root through the internal overload, so the real
/// <c>~/.config/Downloader/plugins</c> is never touched.
/// </summary>
public class PluginManagerRobustnessTests
{
    private sealed class FakePlugin(string id) : IDownloaderPlugin
    {
        public string Id { get; } = id;
        public string Name => "fake " + Id;
        public string Version => "1.0.0";
        public string Author => "test";
        public string Description => "fake";
        public void Initialize(IPluginContext context) { }
    }

    /// <summary>Registers nulls — the context must ignore them rather than storing a null contribution.</summary>
    private sealed class NullRegisteringPlugin : IDownloaderPlugin
    {
        public string Id => "com.test.nulls";
        public string Name => "nulls";
        public string Version => "1.0.0";
        public string Author => "test";
        public string Description => "registers nothing";

        public void Initialize(IPluginContext context)
        {
            context.RegisterResolver(null);
            context.RegisterTransferProvider(null);
            context.RegisterPostProcessor(null);
            context.RegisterPostDownloadAction(null);
        }
    }

    private sealed class ThrowingPlugin : IDownloaderPlugin
    {
        public string Id => "com.test.throws";
        public string Name => "throws";
        public string Version => "1.0.0";
        public string Author => "test";
        public string Description => "fails to initialise";
        public void Initialize(IPluginContext context) => throw new InvalidOperationException("boom");
    }

    // ---- registration guards ----------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_null_or_unidentified_plugin_is_ignored()
    {
        var pm = new PluginManager();

        pm.RegisterPlugin(null);
        pm.RegisterPlugin(new FakePlugin(null));
        pm.RegisterPlugin(new FakePlugin(""));
        pm.RegisterPlugin(new FakePlugin("   "));

        // The id is the key everything else (config, catalog, updates) is written against, so a
        // plugin without one cannot be tracked and must not be loaded.
        Assert.Empty(pm.Plugins);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_plugin_that_throws_while_initialising_is_dropped_not_propagated()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin("com.test.good"));

        pm.RegisterPlugin(new ThrowingPlugin());

        // One bad plugin must not take the app down, nor evict the good ones.
        Assert.Single(pm.Plugins);
        Assert.Equal("com.test.good", pm.Plugins[0].Id);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_plugin_registering_null_contributions_registers_nothing()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new NullRegisteringPlugin());

        Assert.Single(pm.Plugins);
        // It loaded, but contributed nothing — a stored null would throw later, at download time.
        Assert.Null(pm.FindResolver("https://host/x"));
        Assert.Null(pm.FindTransferProvider("https://host/x"));
    }

    // ---- lookups for things that are not there ----------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Lookups_for_an_unclaimed_link_return_nothing()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin("com.test.a"));

        Assert.Null(pm.FindResolver("https://host/file.zip"));
        Assert.Null(pm.FindResolverPluginId("https://host/file.zip"));
        Assert.Null(pm.FindResolverPluginName("https://host/file.zip"));
        Assert.Null(pm.FindPostProcessor(PostProcess.None));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Queries_about_an_unknown_plugin_are_answered_not_thrown()
    {
        var pm = new PluginManager();

        Assert.False(pm.IsInstalled("com.test.missing"));
        Assert.Null(pm.InstalledVersion("com.test.missing"));
        Assert.Empty(pm.GetRuntimeDependencies("com.test.missing"));
        Assert.False(pm.RemovePlugin("com.test.missing"));

        // A blank id must be handled the same way rather than matching something by accident.
        pm.SetEnabled(null, false);
        pm.SetEnabled("", false);
        Assert.False(pm.RemovePlugin(""));
        Assert.Null(pm.InstalledVersion(""));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_plugin_without_declared_dependencies_reports_none()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin("com.test.a"));

        Assert.Empty(pm.GetRuntimeDependencies("com.test.a"));
    }

    // ---- loading from a folder --------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Loading_from_a_missing_or_blank_folder_is_a_no_op()
    {
        var pm = new PluginManager();

        pm.LoadFromDirectory(null);
        pm.LoadFromDirectory("");
        pm.LoadFromDirectory("   ");
        pm.LoadFromDirectory(Path.Combine(Path.GetTempPath(), "no-such-dir-" + Guid.NewGuid().ToString("N")));

        // A fresh install has no plugins folder at all; that is normal, not an error.
        Assert.Empty(pm.Plugins);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_folder_with_nothing_loadable_in_it_yields_no_plugins()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "not a plugin");
            File.WriteAllBytes(Path.Combine(dir, "broken.dll"), new byte[] { 1, 2, 3, 4 });

            var pm = new PluginManager();
            pm.LoadFromDirectory(dir);

            // A stray file — or a DLL that is not a plugin — must be skipped quietly, not crash startup.
            Assert.Empty(pm.Plugins);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    // ---- the install gate --------------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_install_with_no_package_is_refused()
    {
        var pm = new PluginManager();
        var root = NewTempDir();
        try
        {
            var missing = Path.Combine(root, "nope.zip");

            foreach (var zip in new[] { null, "", "   ", missing })
            {
                var result = await pm.InstallFromZipAsync(zip, "abc", "com.test.a", root, TestContext.Current.CancellationToken);
                Assert.False(result.Success);
                Assert.False(string.IsNullOrWhiteSpace(result.Error));
            }
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_install_with_no_checksum_to_verify_against_is_refused()
    {
        var root = NewTempDir();
        try
        {
            var zip = MakeZip(root);
            var pm = new PluginManager();

            foreach (var sha in new[] { null, "", "   " })
            {
                var result = await pm.InstallFromZipAsync(zip, sha, "com.test.a", root, TestContext.Current.CancellationToken);

                // No hash means nothing to verify against, and verification is what stands between a
                // tampered asset and arbitrary code running in-process.
                Assert.False(result.Success);
            }

            Assert.False(Directory.Exists(Path.Combine(root, "com.test.a")));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_package_whose_hash_does_not_match_is_never_extracted()
    {
        var root = NewTempDir();
        try
        {
            var zip = MakeZip(root);
            var pm = new PluginManager();

            var result = await pm.InstallFromZipAsync(zip, new string('a', 64), "com.test.a", root, TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            // Not extracted-then-checked: the folder must not exist at all.
            Assert.False(Directory.Exists(Path.Combine(root, "com.test.a")));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_package_that_verifies_but_carries_no_plugin_is_reported()
    {
        var root = NewTempDir();
        try
        {
            var zip = MakeZip(root);
            var sha = Sha256(zip);
            var pm = new PluginManager();

            var result = await pm.InstallFromZipAsync(zip, sha, "com.test.a", root, TestContext.Current.CancellationToken);

            // The hash is fine, so it extracts — but there is no IDownloaderPlugin inside, and the
            // user needs to be told that rather than seeing a silent no-op.
            Assert.False(result.Success);
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
        }
        finally
        {
            Cleanup(root);
        }
    }

    // ---- helpers -----------------------------------------------------------

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dldesktop-pm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
    }

    /// <summary>A syntactically valid zip that contains no plugin assembly.</summary>
    private static string MakeZip(string root)
    {
        var zip = Path.Combine(root, "package.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("readme.txt");
            using var w = new StreamWriter(entry.Open());
            w.Write("no plugin here");
        }
        return zip;
    }

    private static string Sha256(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }
}
