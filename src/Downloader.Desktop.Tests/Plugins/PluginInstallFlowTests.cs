using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins;

/// <summary>
/// Installing, updating and removing an optional plugin end to end — the asset is served over
/// loopback and installed into a temp plugins root, so this exercises the real download → verify →
/// extract → load path without touching the network or the developer's real plugins folder.
///
/// This flow was previously untestable (and therefore untested) purely because
/// <c>PluginManager.PluginsRoot</c> resolved to the user's own config directory. It is the one code
/// path that fetches a remote archive and loads executable code out of it, so the ordering matters:
/// the checksum is verified BEFORE anything is extracted, and a failed download must not leave the
/// previously-installed copy uninstalled.
/// </summary>
public class PluginInstallFlowTests : IDisposable
{
    private readonly string _root;
    private readonly List<IDisposable> _servers = new();

    public PluginInstallFlowTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dldesktop-plugins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        PluginManager.PluginsRootOverride = _root;
        NotificationService.Enabled = false;
    }

    public void Dispose()
    {
        PluginManager.PluginsRootOverride = null;   // never leave the real root redirected
        foreach (var s in _servers)
            s.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    private sealed class FakePlugin(string id, string version) : IDownloaderPlugin
    {
        public string Id { get; } = id;
        public string Name => Id;
        public string Version { get; } = version;
        public string Author => "test";
        public string Description => "fake";
        public void Initialize(IPluginContext context) { }
    }

    /// <summary>Serves one byte payload at any path, or 404s when told to.</summary>
    private sealed class AssetServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly byte[] _payload;
        private readonly bool _found;

        public AssetServer(byte[] payload, bool found = true)
        {
            _payload = payload;
            _found = found;
            var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            Url = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
            new Thread(Loop) { IsBackground = true }.Start();
        }

        public string Url { get; }

        private void Loop()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch { return; }

                try
                {
                    if (!_found)
                    {
                        ctx.Response.StatusCode = 404;
                        ctx.Response.Close();
                        continue;
                    }

                    ctx.Response.Headers["Accept-Ranges"] = "bytes";
                    ctx.Response.ContentLength64 = _payload.Length;
                    if (ctx.Request.HttpMethod != "HEAD")
                        ctx.Response.OutputStream.Write(_payload, 0, _payload.Length);
                    ctx.Response.Close();
                }
                catch { /* client went away */ }
            }
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }
        }
    }

    private AssetServer Serve(byte[] payload, bool found = true)
    {
        var s = new AssetServer(payload, found);
        _servers.Add(s);
        return s;
    }

    /// <summary>A zip carrying a real, loadable plugin assembly (the built Ollama plugin).</summary>
    private byte[] RealPluginZip()
    {
        var dll = typeof(Downloader.Desktop.Plugins.Ollama.OllamaPlugin).Assembly.Location;
        var staging = Path.Combine(_root, "staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var zipPath = Path.Combine(staging, "plugin.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(dll, Path.GetFileName(dll));
            var deps = Path.ChangeExtension(dll, ".deps.json");
            if (File.Exists(deps))
                archive.CreateEntryFromFile(deps, Path.GetFileName(deps));
        }
        var bytes = File.ReadAllBytes(zipPath);
        Directory.Delete(staging, recursive: true);
        return bytes;
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private static CatalogPluginInfo Entry(string id, string version, string url, string sha) => new()
    {
        Id = id, Name = id, Description = "d", Version = version,
        AssetName = id + ".zip", AssetUrl = url, Sha256 = sha, MinAppVersion = "1.0.0",
    };

    // ---- the whole install path -------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_catalog_plugin_downloads_verifies_and_loads()
    {
        Localizer.Instance.Load("en");
        var zip = RealPluginZip();
        var server = Serve(zip);
        var pm = new PluginManager();

        var result = await PluginCatalogService.InstallOrUpdateAsync(
            pm, Entry("com.bezzad.ollama-models", "1.0.0", server.Url + "p.zip", Sha256(zip)),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.True(pm.IsInstalled("com.bezzad.ollama-models"));
        // Extracted into its own folder under the (temp) plugins root, ready to reload next launch.
        Assert.True(Directory.Exists(Path.Combine(_root, "com.bezzad.ollama-models")));
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_tampered_asset_is_rejected_before_anything_is_extracted()
    {
        Localizer.Instance.Load("en");
        var server = Serve(RealPluginZip());
        var pm = new PluginManager();

        // The catalog's hash does not match what the server actually sent.
        var result = await PluginCatalogService.InstallOrUpdateAsync(
            pm, Entry("com.bezzad.ollama-models", "1.0.0", server.Url + "p.zip", new string('b', 64)),
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.False(pm.IsInstalled("com.bezzad.ollama-models"));
        // Verification happens before extraction precisely so a tampered archive never reaches disk
        // as loadable code.
        Assert.False(Directory.Exists(Path.Combine(_root, "com.bezzad.ollama-models")));
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_download_that_fails_leaves_the_installed_copy_alone()
    {
        Localizer.Instance.Load("en");
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin("com.bezzad.ollama-models", "1.0.0"));
        var server = Serve(Array.Empty<byte>(), found: false);

        var result = await PluginCatalogService.InstallOrUpdateAsync(
            pm, Entry("com.bezzad.ollama-models", "2.0.0", server.Url + "p.zip", new string('c', 64)),
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        // The old copy is removed only AFTER a successful download — otherwise a failed update left
        // the plugin silently uninstalled behind a stale "installed" row.
        Assert.True(pm.IsInstalled("com.bezzad.ollama-models"));
        Assert.Equal("1.0.0", pm.InstalledVersion("com.bezzad.ollama-models"));
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task Installing_over_an_existing_copy_swaps_it()
    {
        Localizer.Instance.Load("en");
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin("com.bezzad.ollama-models", "0.1.0"));

        var zip = RealPluginZip();
        var server = Serve(zip);

        var result = await PluginCatalogService.InstallOrUpdateAsync(
            pm, Entry("com.bezzad.ollama-models", "9.9.9", server.Url + "p.zip", Sha256(zip)),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        // Registration is idempotent by id, so a still-loaded old copy would block the new one — the
        // swap has to unload first.
        Assert.NotEqual("0.1.0", pm.InstalledVersion("com.bezzad.ollama-models"));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Installing_nothing_is_refused()
    {
        var pm = new PluginManager();

        Assert.False((await PluginCatalogService.InstallOrUpdateAsync(null, null,
            TestContext.Current.CancellationToken)).Success);
        Assert.False((await PluginCatalogService.InstallOrUpdateAsync(pm, null,
            TestContext.Current.CancellationToken)).Success);
    }

    // ---- through the Plugins page ------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task Adding_from_the_catalog_moves_the_row_into_the_installed_list()
    {
        Localizer.Instance.Load("en");
        var zip = RealPluginZip();
        var server = Serve(zip);

        var pm = new PluginManager();
        var page = new PluginsViewModel(pm, Config.New());
        page.SetCatalogForTest(new List<CatalogPluginInfo>
        {
            Entry("com.bezzad.ollama-models", "1.0.0", server.Url + "p.zip", Sha256(zip)),
        });

        var row = Assert.Single(page.CatalogPlugins);
        await page.AddFromCatalogAsync(row);

        // It must leave "More plugins" and appear as installed, without a restart.
        Assert.Contains(page.Plugins, p => p.Id == "com.bezzad.ollama-models");
        Assert.DoesNotContain(page.CatalogPlugins, c => c.Id == "com.bezzad.ollama-models");
        Assert.False(row.IsBusy);
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_failed_add_keeps_the_row_and_shows_why()
    {
        Localizer.Instance.Load("en");
        var server = Serve(Array.Empty<byte>(), found: false);

        var pm = new PluginManager();
        var page = new PluginsViewModel(pm, Config.New());
        page.SetCatalogForTest(new List<CatalogPluginInfo>
        {
            Entry("com.test.b", "1.0.0", server.Url + "p.zip", new string('d', 64)),
        });

        var row = Assert.Single(page.CatalogPlugins);
        await page.AddFromCatalogAsync(row);

        // The offer stays so the user can retry, and the reason is shown inline rather than
        // disappearing into the log.
        Assert.Contains(page.CatalogPlugins, c => c.Id == "com.test.b");
        Assert.False(row.IsBusy);
        Assert.False(string.IsNullOrWhiteSpace(row.ErrorText));
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task Updating_an_installed_plugin_goes_through_the_same_path()
    {
        Localizer.Instance.Load("en");
        var zip = RealPluginZip();
        var server = Serve(zip);

        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin("com.bezzad.ollama-models", "0.1.0"));
        var page = new PluginsViewModel(pm, Config.New());
        page.SetCatalogForTest(new List<CatalogPluginInfo>
        {
            Entry("com.bezzad.ollama-models", "9.9.9", server.Url + "p.zip", Sha256(zip)),
        });

        var row = Assert.Single(page.Plugins);
        Assert.True(row.UpdateAvailable);

        await page.UpdateInstalledAsync(row);

        Assert.True(pm.IsInstalled("com.bezzad.ollama-models"));
        Assert.False(page.Plugins.Single().IsBusy);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Adding_a_row_that_is_already_busy_is_ignored()
    {
        Localizer.Instance.Load("en");
        var page = new PluginsViewModel(new PluginManager(), Config.New());
        page.SetCatalogForTest(new List<CatalogPluginInfo>
        {
            Entry("com.test.b", "1.0.0", "http://127.0.0.1:1/p.zip", new string('e', 64)),
        });

        var row = Assert.Single(page.CatalogPlugins);
        row.IsBusy = true;

        await page.AddFromCatalogAsync(row);   // a double-click must not start a second download
        await page.AddFromCatalogAsync(null);

        Assert.True(row.IsBusy);
    }

    // ---- removing ----------------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task Removing_an_installed_plugin_deletes_it_from_disk()
    {
        Localizer.Instance.Load("en");
        var zip = RealPluginZip();
        var server = Serve(zip);
        var pm = new PluginManager();

        await PluginCatalogService.InstallOrUpdateAsync(
            pm, Entry("com.bezzad.ollama-models", "1.0.0", server.Url + "p.zip", Sha256(zip)),
            TestContext.Current.CancellationToken);
        Assert.True(pm.IsInstalled("com.bezzad.ollama-models"));

        Assert.True(pm.RemovePlugin("com.bezzad.ollama-models"));

        // It must stop contributing immediately AND not come back on the next launch.
        Assert.False(pm.IsInstalled("com.bezzad.ollama-models"));
        var pm2 = new PluginManager();
        pm2.LoadFromDirectory(_root);
        Assert.False(pm2.IsInstalled("com.bezzad.ollama-models"));
    }
}
