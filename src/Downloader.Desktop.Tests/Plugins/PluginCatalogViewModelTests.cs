using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins;

/// <summary>
/// PluginsViewModel catalog behavior (consolidate-official-plugins): the "More plugins" list shows only
/// not-installed optional plugins (Add-only), installed rows get an update badge when the catalog is
/// newer, and a failed Add surfaces an inline error without removing the row. Network-free via the
/// catalog test seam and an unreachable asset URL.
/// </summary>
public class PluginCatalogViewModelTests
{
    private sealed class FakePlugin(string id, string version) : IDownloaderPlugin
    {
        public string Id { get; } = id;
        public string Name => id;
        public string Version { get; } = version;
        public string Author => "test";
        public string Description => "fake";
        public void Initialize(IPluginContext context) { }
    }

    private static CatalogPluginInfo Entry(string id, string version, string assetUrl = "https://example.com/x.zip") =>
        new()
        {
            Id = id, Name = id, Description = "d", Version = version,
            AssetName = $"{id}.zip", AssetUrl = assetUrl, Sha256 = "abc", MinAppVersion = "1.0.0",
        };

    [AvaloniaFact]
    public void Catalog_lists_only_uninstalled_and_flags_updates_on_installed()
    {
        Localizer.Instance.Load("en");
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin("com.test.a", "1.0.0")); // installed, older than catalog
        var vm = new PluginsViewModel(pm, Config.New());

        vm.SetCatalogForTest(new List<CatalogPluginInfo>
        {
            Entry("com.test.a", "2.0.0"), // installed → should flag an update, NOT appear in "More plugins"
            Entry("com.test.b", "1.0.0"), // not installed → should appear as an Add-only catalog row
        });

        // "More plugins" shows only the uninstalled one.
        Assert.True(vm.HasCatalog);
        var row = Assert.Single(vm.CatalogPlugins);
        Assert.Equal("com.test.b", row.Id);
        Assert.NotNull(row.AddCommand); // Add-only surface (no enable/remove on a catalog row)

        // Installed row got an update badge pointing at the newer catalog entry.
        var installed = Assert.Single(vm.Plugins);
        Assert.Equal("com.test.a", installed.Id);
        Assert.True(installed.UpdateAvailable);
        Assert.Equal("2.0.0", installed.PendingUpdate!.Version);
    }

    [AvaloniaFact]
    public async Task Adding_a_catalog_plugin_that_cannot_download_shows_an_error_and_keeps_the_row()
    {
        Localizer.Instance.Load("en");
        var pm = new PluginManager();
        var vm = new PluginsViewModel(pm, Config.New());

        // Unreachable/empty asset URL → download fails fast (no network), surfacing the error state.
        vm.SetCatalogForTest(new List<CatalogPluginInfo> { Entry("com.test.b", "1.0.0", assetUrl: "") });
        var row = Assert.Single(vm.CatalogPlugins);

        await vm.AddFromCatalogAsync(row);

        Assert.True(row.HasError);
        Assert.False(string.IsNullOrWhiteSpace(row.ErrorText));
        Assert.False(row.IsBusy);
        Assert.Contains(row, vm.CatalogPlugins);      // still offered — the add didn't succeed
        Assert.Empty(pm.Plugins);                     // nothing loaded
    }
}
