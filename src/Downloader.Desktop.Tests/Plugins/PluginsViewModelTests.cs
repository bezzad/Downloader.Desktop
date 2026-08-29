using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins;

/// <summary>
/// The Plugins section in Settings: the installed list, the enable/disable toggle, removal, and the
/// catalog version rules.
///
/// Enabling and disabling is the part with teeth — a disabled plugin must stop contributing resolvers
/// immediately (not at the next restart) and the choice must be written to the config, since that is
/// the only record of it. Removal has to put a catalog-tier plugin straight back into "More plugins"
/// with an Add button; before that was wired it vanished from BOTH lists until the app restarted.
///
/// Everything runs against in-process fake plugins, so no DLL is loaded and no network is touched.
/// </summary>
public class PluginsViewModelTests
{
    private sealed class FakePlugin(string id, string version) : IDownloaderPlugin
    {
        public string Id { get; } = id;
        public string Name => Id;
        public string Version { get; } = version;
        public string Author => "test";
        public string Description => "a fake plugin";
        public void Initialize(IPluginContext context) { }
    }

    private static CatalogPluginInfo Entry(string id, string version, string minApp = "1.0.0") =>
        new()
        {
            Id = id, Name = id, Description = "d", Version = version,
            AssetName = $"{id}.zip", AssetUrl = "https://10.255.255.1/x.zip",
            Sha256 = "abc", MinAppVersion = minApp,
        };

    // ---- installed list ----------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_install_with_no_plugins_reports_empty()
    {
        Localizer.Instance.Load("en");
        var vm = new PluginsViewModel(new PluginManager(), Config.New());

        Assert.True(vm.IsEmpty);
        Assert.Empty(vm.Plugins);
        Assert.False(vm.HasCatalog);
        Assert.False(vm.IsCatalogLoading);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_installed_plugin_shows_its_identity()
    {
        Localizer.Instance.Load("en");
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin("com.test.a", "1.2.3"));
        var vm = new PluginsViewModel(pm, Config.New());

        Assert.False(vm.IsEmpty);
        var row = Assert.Single(vm.Plugins);
        Assert.Equal("com.test.a", row.Id);
        Assert.Equal("com.test.a", row.Name);
        Assert.Equal("test", row.Author);
        Assert.Equal("a fake plugin", row.Description);
        Assert.Equal("v1.2.3", row.VersionText);
        Assert.False(row.UpdateAvailable);
        Assert.Null(row.PendingUpdate);
        Assert.False(row.IsBusy);
    }

    // ---- enable / disable --------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Disabling_a_plugin_records_it_and_stops_its_contributions()
    {
        Localizer.Instance.Load("en");
        var config = Config.New();
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin("com.test.a", "1.0.0"));
        var vm = new PluginsViewModel(pm, config);
        var row = vm.Plugins.Single();

        Assert.True(row.IsEnabled);

        row.IsEnabled = false;

        Assert.False(row.IsEnabled);
        // The config's disabled list is the only record of the choice across restarts.
        Assert.Contains("com.test.a", config.DisabledPlugins);
        Assert.False(pm.Plugins.Single(p => p.Id == "com.test.a").IsEnabled);

        row.IsEnabled = true;

        Assert.True(row.IsEnabled);
        Assert.DoesNotContain("com.test.a", config.DisabledPlugins);
        Assert.True(pm.Plugins.Single(p => p.Id == "com.test.a").IsEnabled);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Toggling_the_same_value_twice_does_not_duplicate_the_disabled_entry()
    {
        Localizer.Instance.Load("en");
        var config = Config.New();
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin("com.test.a", "1.0.0"));
        var row = new PluginsViewModel(pm, config).Plugins.Single();

        row.IsEnabled = false;
        row.IsEnabled = false;

        Assert.Single(config.DisabledPlugins, id => id == "com.test.a");
    }

    // ---- removal -----------------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Removing_a_catalog_plugin_puts_it_straight_back_on_offer()
    {
        Localizer.Instance.Load("en");
        var config = Config.New();
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin("com.test.a", "1.0.0"));
        var vm = new PluginsViewModel(pm, config);
        vm.SetCatalogForTest(new List<CatalogPluginInfo> { Entry("com.test.a", "1.0.0") });

        // Installed, so it is not on offer yet.
        Assert.Empty(vm.CatalogPlugins);
        var row = vm.Plugins.Single();

        row.RemoveCommand.Execute(null);

        Assert.Empty(vm.Plugins);
        Assert.True(vm.IsEmpty);
        // …and it must reappear under "More plugins" immediately, not after a restart.
        Assert.Contains(vm.CatalogPlugins, c => c.Id == "com.test.a");
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Removing_a_plugin_also_clears_its_disabled_flag()
    {
        Localizer.Instance.Load("en");
        var config = Config.New();
        // Config.New() leaves this null; the view model null-coalesces it, so seed it explicitly.
        config.DisabledPlugins = new List<string> { "com.test.a" };
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin("com.test.a", "1.0.0"));
        var vm = new PluginsViewModel(pm, config);

        vm.Plugins.Single().RemoveCommand.Execute(null);

        // Otherwise a later re-install would silently come back disabled.
        Assert.DoesNotContain("com.test.a", config.DisabledPlugins);
    }

    // ---- catalog version rules --------------------------------------------

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("2.0.0", "1.0.0", true)]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "2.0.0", false)]
    [InlineData(null, "1.0.0", false)]
    [InlineData("2.0.0", null, false)]
    [InlineData("junk", "1.0.0", false)]
    public void A_catalog_entry_is_an_update_only_when_it_is_strictly_newer(
        string? catalogVersion, string? installedVersion, bool expected)
    {
        // A false positive here makes the update prompt reappear forever.
        Assert.Equal(expected, PluginCatalogService.IsNewer(catalogVersion, installedVersion));
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("1.0.0", "2.0.0", true)]   // app newer than required
    [InlineData("2.0.0", "2.0.0", true)]   // exactly the required version
    [InlineData("3.0.0", "2.0.0", false)]  // app too old
    [InlineData(null, "2.0.0", true)]      // no minimum declared
    [InlineData("", "2.0.0", true)]
    [InlineData("junk", "2.0.0", true)]    // unparsable minimum is permissive
    public void A_catalog_entry_is_hidden_when_the_app_is_too_old(
        string? minAppVersion, string appVersion, bool expected)
    {
        Assert.Equal(expected,
            PluginCatalogService.MeetsMinAppVersion(minAppVersion, Version.Parse(appVersion)));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_entry_requiring_a_newer_app_never_reaches_the_more_plugins_list()
    {
        Localizer.Instance.Load("en");
        var vm = new PluginsViewModel(new PluginManager(), Config.New());

        vm.SetCatalogForTest(new List<CatalogPluginInfo>
        {
            Entry("com.test.future", "1.0.0", minApp: "999.0.0"),
            Entry("com.test.ok", "1.0.0", minApp: "1.0.0"),
        });

        // Offering it would install a plugin whose host plumbing this build lacks, so its downloads
        // could never work.
        Assert.DoesNotContain(vm.CatalogPlugins, c => c.Id == "com.test.future");
        Assert.Contains(vm.CatalogPlugins, c => c.Id == "com.test.ok");
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_empty_or_null_catalog_leaves_the_section_hidden()
    {
        Localizer.Instance.Load("en");
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin("com.test.a", "1.0.0"));
        var vm = new PluginsViewModel(pm, Config.New());

        vm.SetCatalogForTest(new List<CatalogPluginInfo> { Entry("com.test.b", "1.0.0") });
        Assert.True(vm.HasCatalog);

        vm.SetCatalogForTest(null);

        Assert.False(vm.HasCatalog);
        Assert.Empty(vm.CatalogPlugins);
        Assert.False(vm.Plugins.Single().UpdateAvailable); // badges cleared with the catalog
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_catalog_row_exposes_what_the_add_button_needs()
    {
        Localizer.Instance.Load("en");
        var vm = new PluginsViewModel(new PluginManager(), Config.New());
        vm.SetCatalogForTest(new List<CatalogPluginInfo> { Entry("com.test.b", "1.4.0") });

        var row = Assert.Single(vm.CatalogPlugins);
        Assert.Equal("com.test.b", row.Id);
        Assert.Equal("com.test.b", row.Name);
        Assert.Equal("d", row.Description);
        Assert.NotNull(row.AddCommand);
        Assert.NotNull(row.CancelCommand);
        Assert.False(row.IsBusy);
        Assert.Null(row.ErrorText);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_update_badge_follows_the_catalog_version()
    {
        Localizer.Instance.Load("en");
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin("com.test.a", "1.0.0"));
        var vm = new PluginsViewModel(pm, Config.New());

        vm.SetCatalogForTest(new List<CatalogPluginInfo> { Entry("com.test.a", "2.0.0") });
        var row = vm.Plugins.Single();
        Assert.True(row.UpdateAvailable);
        Assert.Equal("2.0.0", row.PendingUpdate.Version);

        // Same version → no badge (otherwise the prompt would never stop).
        vm.SetCatalogForTest(new List<CatalogPluginInfo> { Entry("com.test.a", "1.0.0") });
        Assert.False(vm.Plugins.Single().UpdateAvailable);
    }
}
