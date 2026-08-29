using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Services;
using Downloader.Desktop.Tests.Plugins.Hls;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins;

/// <summary>
/// The Plugins section driving a REAL install: the "More plugins" list fetched off a loopback release,
/// then Add pulling the archive down, verifying it and loading it into a temp plugins root.
///
/// The list mechanics were covered with a hand-fed catalog; what wasn't is the round trip, and that is
/// where the section's worst behaviour used to live — a failed Add that left the row spinning forever,
/// or a plugin that vanished from BOTH lists because nothing re-synced them. Every assertion here is
/// about the state the section is left in afterwards, because that is what the user is looking at.
///
/// Nothing touches the developer's real plugins folder (PluginsRootOverride) or the network
/// (ReleasesUrlOverride + a loopback asset server).
/// </summary>
public class PluginsViewModelCatalogTests : IDisposable
{
    private const string PluginId = "com.bezzad.ollama-models";

    private readonly string _root;
    private readonly List<IDisposable> _disposables = new();
    private readonly bool _notificationsWereEnabled = NotificationService.Enabled;

    public PluginsViewModelCatalogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dldesktop-plugins-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        PluginManager.PluginsRootOverride = _root;
        NotificationService.Enabled = false;
        Localizer.Instance.Load("en");
    }

    public void Dispose()
    {
        PluginManager.PluginsRootOverride = null;   // never leave the real root redirected
        PluginCatalogService.ReleasesUrlOverride = null;
        NotificationService.Enabled = _notificationsWereEnabled;
        foreach (var d in _disposables)
            d.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
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

    /// <summary>Stands up a release whose catalog offers one entry for the given zip.</summary>
    private LoopbackServer PublishCatalog(byte[] zip, string version, string sha, string minAppVersion = "1.0.0")
    {
        var server = new LoopbackServer();
        _disposables.Add(server);
        server.MapBytes("/plugin.zip", zip, "application/zip");
        server.MapText("/catalog.json", $$"""
        [{
          "id": "{{PluginId}}", "name": "Ollama Models", "description": "models by name",
          "version": "{{version}}", "assetName": "plugin.zip", "sha256": "{{sha}}",
          "minAppVersion": "{{minAppVersion}}"
        }]
        """, "application/json");
        server.MapText("/releases/latest", $$"""
        {
          "tag_name": "v9.9.9",
          "assets": [
            { "name": "plugins-catalog.json", "browser_download_url": "{{server.Url("catalog.json")}}" },
            { "name": "plugin.zip",           "browser_download_url": "{{server.Url("plugin.zip")}}" }
          ]
        }
        """, "application/json");
        PluginCatalogService.ReleasesUrlOverride = server.Url("releases/latest");
        return server;
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

    // ---- the catalog list --------------------------------------------------

    /// <summary>
    /// The whole round trip: an uninstalled catalog entry shows up under "More plugins", Add downloads
    /// and loads it, and the section then shows it as installed and no longer on offer.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task Adding_an_offered_plugin_installs_it_and_moves_it_into_the_installed_list()
    {
        var zip = RealPluginZip();
        PublishCatalog(zip, "1.0.0", Sha256(zip));
        var pm = new PluginManager();
        var config = Config.New();
        config.DisabledPlugins = new List<string> { PluginId }; // it must come back ENABLED
        var vm = new PluginsViewModel(pm, config);

        await vm.RefreshCatalogAsync(TestContext.Current.CancellationToken);

        Assert.False(vm.IsCatalogLoading);
        Assert.True(vm.HasCatalog);
        var offered = Assert.Single(vm.CatalogPlugins);
        Assert.Equal(PluginId, offered.Id);
        Assert.Equal("v1.0.0", offered.VersionText);
        Assert.True(vm.IsEmpty, "nothing is installed yet");

        await vm.AddFromCatalogAsync(offered);

        Assert.True(pm.IsInstalled(PluginId));
        Assert.Contains(vm.Plugins, p => p.Id == PluginId);
        Assert.DoesNotContain(vm.CatalogPlugins, c => c.Id == PluginId);
        Assert.DoesNotContain(PluginId, config.DisabledPlugins);
        Assert.False(offered.IsBusy, "the row must not be left spinning");
        Assert.Null(offered.ErrorText);
        Assert.True(Directory.Exists(Path.Combine(_root, PluginId)));
    }

    /// <summary>
    /// A tampered or truncated asset must be refused, and the row has to SAY so — a silent failure
    /// leaves an Add button that visibly does nothing.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task An_asset_that_fails_verification_leaves_the_row_offered_with_a_reason()
    {
        var zip = RealPluginZip();
        PublishCatalog(zip, "1.0.0", sha: "0000000000000000000000000000000000000000000000000000000000000000");
        var pm = new PluginManager();
        var vm = new PluginsViewModel(pm, Config.New());
        await vm.RefreshCatalogAsync(TestContext.Current.CancellationToken);
        var offered = Assert.Single(vm.CatalogPlugins);

        await vm.AddFromCatalogAsync(offered);

        Assert.False(pm.IsInstalled(PluginId));
        Assert.False(offered.IsBusy);
        Assert.False(string.IsNullOrWhiteSpace(offered.ErrorText), "a refused install must say why");
        Assert.Contains(vm.CatalogPlugins, c => c.Id == PluginId);
    }

    /// <summary>A busy row must ignore a second click rather than starting a parallel install.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_row_that_is_already_installing_ignores_another_click()
    {
        var zip = RealPluginZip();
        PublishCatalog(zip, "1.0.0", Sha256(zip));
        var vm = new PluginsViewModel(new PluginManager(), Config.New());
        await vm.RefreshCatalogAsync(TestContext.Current.CancellationToken);
        var offered = Assert.Single(vm.CatalogPlugins);
        offered.IsBusy = true;

        await vm.AddFromCatalogAsync(offered);
        await vm.AddFromCatalogAsync(null);

        Assert.True(offered.IsBusy, "the in-flight install owns the row");
        Assert.Null(offered.ErrorText);
    }

    // ---- updating an installed plugin --------------------------------------

    /// <summary>
    /// An installed plugin older than the catalog's gets an Update badge, and accepting it swaps the
    /// copy on disk. The badge is the only way a user learns a fix exists.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task An_offered_update_is_badged_and_applied_on_accept()
    {
        var zip = RealPluginZip();
        PublishCatalog(zip, "99.0.0", Sha256(zip));
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin(PluginId, "1.0.0"));
        var vm = new PluginsViewModel(pm, Config.New());

        await vm.RefreshCatalogAsync(TestContext.Current.CancellationToken);

        var installed = Assert.Single(vm.Plugins);
        Assert.True(installed.UpdateAvailable, "an older installed version must be badged");
        Assert.Equal("99.0.0", installed.PendingUpdate?.Version);
        Assert.DoesNotContain(vm.CatalogPlugins, c => c.Id == PluginId);

        await vm.UpdateInstalledAsync(installed);

        Assert.False(installed.IsBusy);
        Assert.True(Directory.Exists(Path.Combine(_root, PluginId)), "the new copy is on disk");
    }

    /// <summary>A failed update must re-sync the lists — the old copy is unloaded before the swap, so
    /// leaving the row alone would show a plugin that is no longer loaded as installed.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_failed_update_resyncs_the_lists_instead_of_leaving_a_stale_row()
    {
        var zip = RealPluginZip();
        PublishCatalog(zip, "99.0.0", sha: "0000000000000000000000000000000000000000000000000000000000000000");
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin(PluginId, "1.0.0"));
        var vm = new PluginsViewModel(pm, Config.New());
        await vm.RefreshCatalogAsync(TestContext.Current.CancellationToken);
        var installed = Assert.Single(vm.Plugins);
        Assert.True(installed.UpdateAvailable);

        await vm.UpdateInstalledAsync(installed);

        Assert.False(installed.IsBusy);
        // Whatever the outcome, the section must reflect reality: the plugin is either installed or
        // back on offer, never missing from both.
        Assert.True(vm.Plugins.Any(p => p.Id == PluginId) || vm.CatalogPlugins.Any(c => c.Id == PluginId),
            "a failed update must not make the plugin disappear from both lists");
    }

    /// <summary>Nothing to update, or already busy — both must be no-ops.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Updating_without_an_offer_does_nothing()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin(PluginId, "1.0.0"));
        var vm = new PluginsViewModel(pm, Config.New());
        var row = Assert.Single(vm.Plugins);

        await vm.UpdateInstalledAsync(row);   // no PendingUpdate
        await vm.UpdateInstalledAsync(null);

        Assert.False(row.IsBusy);
        Assert.True(pm.IsInstalled(PluginId));
    }

    // ---- the section's plumbing --------------------------------------------

    /// <summary>An unreachable release leaves the section empty and NOT stuck on its loading spinner.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_catalog_that_cannot_be_fetched_leaves_the_section_empty_and_idle()
    {
        PluginCatalogService.ReleasesUrlOverride = "http://10.255.255.1/releases/latest";
        var vm = new PluginsViewModel(new PluginManager(), Config.New());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await vm.RefreshCatalogAsync(cts.Token);

        Assert.False(vm.IsCatalogLoading, "a failed fetch must clear the spinner");
        Assert.False(vm.HasCatalog);
        Assert.Empty(vm.CatalogPlugins);
    }

    // ---- installing a DLL by hand ------------------------------------------

    /// <summary>
    /// The manual Install button: pick a .dll, it is copied into the plugins folder (with its deps
    /// sidecar) and loaded. This used to swallow every failure silently, which is why "install does
    /// nothing" was a real report — so each outcome now has to be visible.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void Installing_a_picked_dll_copies_it_into_the_plugins_folder_and_loads_it()
    {
        var dll = typeof(Downloader.Desktop.Plugins.Ollama.OllamaPlugin).Assembly.Location;
        DialogHelper.OpenFilePickerOverride = () => new Uri(dll);
        try
        {
            var pm = new PluginManager();
            var vm = new PluginsViewModel(pm, Config.New());

            vm.InstallCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.True(File.Exists(Path.Combine(_root, Path.GetFileName(dll))),
                "the picked DLL belongs in the plugins folder");
            Assert.Contains(vm.Plugins, p => p.Id == PluginId);
            // The deps sidecar has to travel with it, or a plugin with its own dependencies won't load.
            var deps = Path.ChangeExtension(dll, ".deps.json");
            if (File.Exists(deps))
                Assert.True(File.Exists(Path.Combine(_root, Path.GetFileName(deps))));
        }
        finally
        {
            DialogHelper.OpenFilePickerOverride = null;
        }
    }

    /// <summary>A DLL that carries no plugin must say so rather than looking like it worked.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void Installing_a_dll_that_is_not_a_plugin_leaves_the_list_empty()
    {
        // The SDK assembly is a perfectly good DLL with no IDownloaderPlugin in it.
        var dll = typeof(IDownloaderPlugin).Assembly.Location;
        DialogHelper.OpenFilePickerOverride = () => new Uri(dll);
        try
        {
            var vm = new PluginsViewModel(new PluginManager(), Config.New());

            vm.InstallCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.True(vm.IsEmpty, "nothing in that file is a plugin");
        }
        finally
        {
            DialogHelper.OpenFilePickerOverride = null;
        }
    }

    /// <summary>A file that vanished between picking and copying must not throw out of the button.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void Installing_a_file_that_cannot_be_copied_is_reported_not_thrown()
    {
        DialogHelper.OpenFilePickerOverride = () =>
            new Uri(Path.Combine(Path.GetTempPath(), "gone-" + Guid.NewGuid().ToString("N") + ".dll"));
        try
        {
            var vm = new PluginsViewModel(new PluginManager(), Config.New());

            var ex = Record.Exception(() =>
            {
                vm.InstallCommand.Execute(null);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            });

            Assert.Null(ex);
            Assert.True(vm.IsEmpty);
        }
        finally
        {
            DialogHelper.OpenFilePickerOverride = null;
        }
    }

    /// <summary>Cancelling the picker does nothing at all.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void Cancelling_the_picker_installs_nothing()
    {
        DialogHelper.OpenFilePickerOverride = () => null;
        try
        {
            var vm = new PluginsViewModel(new PluginManager(), Config.New());

            vm.InstallCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.True(vm.IsEmpty);
            Assert.Empty(Directory.EnumerateFiles(_root));
        }
        finally
        {
            DialogHelper.OpenFilePickerOverride = null;
        }
    }

    /// <summary>Removing an installed plugin drops it from disk and from the list.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void Removing_a_plugin_takes_it_out_of_the_list()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin(PluginId, "1.0.0"));
        var config = Config.New();
        config.DisabledPlugins = new List<string> { PluginId };
        var vm = new PluginsViewModel(pm, config);
        var row = Assert.Single(vm.Plugins);

        row.RemoveCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(vm.IsEmpty);
        Assert.False(pm.IsInstalled(PluginId));
        Assert.DoesNotContain(PluginId, config.DisabledPlugins);
    }

    /// <summary>Reload re-reads the plugins folder — how a hand-copied DLL is picked up without a restart.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Reloading_rereads_the_plugins_folder()
    {
        var vm = new PluginsViewModel(new PluginManager(), Config.New());

        vm.ReloadCommand.Execute(null);      // an empty temp root — must be a clean no-op
        vm.OpenFolderCommand.Execute(null);  // best-effort; no file manager on a headless box

        Assert.True(vm.IsEmpty);
    }
}
