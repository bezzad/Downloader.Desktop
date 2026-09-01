using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// The install dialog's decisions, with no browser, no release and no network.
///
/// The one that matters most is <see cref="Unpacking_alone_never_claims_the_extension_is_connected"/>: the
/// whole dialog rests on showing the user it worked rather than telling them it did, and a green tick that
/// only meant "we unzipped something" would be worse than no tick at all.
/// </summary>
public class ExtensionInstallViewModelTests
{
    private static DetectedBrowser Chrome => new()
    {
        Id = "chrome", Name = "Google Chrome", Family = BrowserFamily.Chromium, ExecutablePath = "/usr/bin/google-chrome",
    };

    private static DetectedBrowser Firefox => new()
    {
        Id = "firefox", Name = "Mozilla Firefox", Family = BrowserFamily.Gecko, ExecutablePath = "/usr/bin/firefox",
    };

    private static ExtensionCatalogEntry Entry(string id, string family, string version = "1.8.0", string storeUrl = null)
        => new()
        {
            Id = id, Family = family, Name = id, Version = version,
            AssetName = $"{id}.zip", AssetUrl = $"https://example.test/{id}.zip",
            Sha256 = new string('a', 64), MinAppVersion = "1.0.0", StoreUrl = storeUrl,
        };

    /// <summary>A VM with every OS/network edge stubbed; `install` defaults to succeeding.</summary>
    private static ExtensionInstallViewModel Vm(
        IEnumerable<DetectedBrowser> browsers = null,
        IEnumerable<ExtensionCatalogEntry> catalog = null,
        Func<ExtensionCatalogEntry, IProgress<double>, CancellationToken, Task<ExtensionInstallResult>> install = null,
        Func<string, string> lastSeen = null,
        Func<string, string> installedPath = null)
        => new(
            detect: () => (browsers ?? new[] { Chrome }).ToList(),
            fetchCatalog: _ => Task.FromResult<IReadOnlyList<ExtensionCatalogEntry>>(
                (catalog ?? new[] { Entry("chrome", "chromium") }).ToList()),
            install: install ?? ((e, p, _) =>
            {
                p?.Report(1.0);
                return Task.FromResult(ExtensionInstallResult.Ok($"/data/extension/{e.Id}", e.Version));
            }),
            lastSeenVersion: lastSeen ?? (_ => null),
            installedPath: installedPath ?? (_ => null));

    // ---- listing ----

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Detected_browsers_are_listed_and_preselected()
    {
        var vm = Vm(new[] { Chrome, Firefox }, new[] { Entry("chrome", "chromium"), Entry("firefox", "gecko") });

        await vm.LoadAsync();

        Assert.Equal(new[] { "Google Chrome", "Mozilla Firefox" }, vm.Browsers.Select(b => b.Name));
        Assert.All(vm.Browsers, b => Assert.True(b.IsSelected));   // the user opened the dialog to install
        Assert.True(vm.HasBrowsers);
        Assert.False(vm.HasNotice);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_family_with_no_detected_browser_gets_no_target()
    {
        // Offering a Firefox build to a machine with no Gecko browser is noise.
        var vm = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium"), Entry("firefox", "gecko") });

        await vm.LoadAsync();

        Assert.Equal(new[] { BrowserFamily.Chromium }, vm.Targets.Select(t => t.Family));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task No_browsers_found_is_a_notice_not_an_error()
    {
        var vm = Vm(Array.Empty<DetectedBrowser>());

        await vm.LoadAsync();

        Assert.False(vm.HasBrowsers);
        Assert.True(vm.HasNotice);
        Assert.False(vm.HasError);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_empty_catalog_says_so_without_erroring()
    {
        // Offline, no release, or every build needs a newer app — all three land here.
        var vm = Vm(new[] { Chrome }, Array.Empty<ExtensionCatalogEntry>());

        await vm.LoadAsync();

        Assert.True(vm.HasNotice);
        Assert.False(vm.HasError);
        Assert.False(vm.CanInstall);
        Assert.False(Assert.Single(vm.Targets).HasBuild);
    }

    // ---- store path vs manual path ----

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_published_listing_makes_the_store_the_primary_path()
    {
        var vm = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium", storeUrl: "https://store.example/x") });

        await vm.LoadAsync();

        var target = Assert.Single(vm.Targets);
        Assert.True(target.UseStore);
        Assert.Equal("https://store.example/x", target.StoreUrl);
        Assert.False(vm.CanInstall);   // nothing to unpack: the store installs and updates it
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task No_listing_makes_the_manual_path_primary()
    {
        var vm = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium") });

        await vm.LoadAsync();

        var target = Assert.Single(vm.Targets);
        Assert.False(target.UseStore);
        Assert.Null(target.StoreUrl);   // never a dead link
        Assert.True(vm.CanInstall);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Opening_the_store_launches_that_browser_at_that_url()
    {
        var launched = new List<(string File, string[] Args)>();
        ShellLauncher.RunOverride = (file, args) => { launched.Add((file, args)); return true; };
        try
        {
            var vm = Vm(new[] { Chrome, Firefox }, new[]
            {
                Entry("chrome", "chromium", storeUrl: "https://chromestore.example/x"),
                Entry("firefox", "gecko", storeUrl: "https://amo.example/y"),
            });
            await vm.LoadAsync();

            vm.Browsers.Single(b => b.Id == "firefox").OpenStoreCommand.Execute(null);

            // Picking a browser has to mean the extension lands in THAT browser, not in whichever one
            // happens to be the machine's default.
            var (file, args) = Assert.Single(launched);
            Assert.Equal("/usr/bin/firefox", file);
            Assert.Equal(new[] { "https://amo.example/y" }, args);
        }
        finally
        {
            ShellLauncher.RunOverride = null;   // process-wide seam
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Opening_the_store_does_nothing_when_there_is_no_listing()
    {
        var launched = 0;
        ShellLauncher.RunOverride = (_, _) => { launched++; return true; };
        ShellLauncher.OpenOverride = _ => { launched++; return true; };
        try
        {
            var vm = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium") });
            await vm.LoadAsync();

            vm.Browsers.Single().OpenStoreCommand.Execute(null);

            Assert.Equal(0, launched);
        }
        finally
        {
            ShellLauncher.RunOverride = null;
            ShellLauncher.OpenOverride = null;
        }
    }

    // ---- the steps and the limitations ----

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Every_family_carries_steps_and_stated_limitations()
    {
        Localizer.Instance.Load("en");   // these assert on real text, not raw keys
        var vm = Vm(new[] { Chrome, Firefox }, new[] { Entry("chrome", "chromium"), Entry("firefox", "gecko") });

        await vm.LoadAsync();

        foreach (var target in vm.Targets)
        {
            Assert.Equal(3, target.Steps.Count);
            Assert.All(target.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s)));
            Assert.False(string.IsNullOrWhiteSpace(target.Limitations));
            // A raw key showing through means the string never made it into en.json.
            Assert.All(target.Steps, s => Assert.DoesNotContain("Ext_Steps_", s));
            Assert.DoesNotContain("Ext_Limits_", target.Limitations);
        }
    }

    /// <summary>
    /// The Gecko limitation MUST be stated: a manually loaded add-on is removed when the browser restarts,
    /// and there is no permanent unsigned install on a release build. A user who is not told reports it.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_gecko_limitation_says_the_extension_is_removed_on_restart()
    {
        Localizer.Instance.Load("en");   // these assert on real text, not raw keys
        var vm = Vm(new[] { Firefox }, new[] { Entry("firefox", "gecko") });

        await vm.LoadAsync();

        Assert.Contains("restart", Assert.Single(vm.Targets).Limitations, StringComparison.OrdinalIgnoreCase);
    }

    // ---- connected / out of date ----

    /// <summary>The show-don't-claim rule, in one test.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Unpacking_alone_never_claims_the_extension_is_connected()
    {
        var vm = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium") }, lastSeen: _ => null);
        await vm.LoadAsync();

        await vm.InstallSelectedAsync();

        Assert.True(Assert.Single(vm.Targets).IsUnpacked);   // the files really are there…
        Assert.False(vm.Browsers.Single().IsConnected);      // …and that proves nothing about the browser
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_browser_that_has_called_is_shown_connected_with_its_version()
    {
        Localizer.Instance.Load("en");   // these assert on real text, not raw keys
        var vm = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium", version: "1.8.0") },
            lastSeen: id => id == "chrome" ? "1.8.0" : null);

        await vm.LoadAsync();

        var row = vm.Browsers.Single();
        Assert.True(row.IsConnected);
        Assert.Equal("1.8.0", row.ConnectedVersion);
        Assert.False(row.UpdateAvailable);
        Assert.Contains("1.8.0", row.StatusText);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_update_is_flagged_only_when_the_reported_version_is_older()
    {
        Localizer.Instance.Load("en");   // these assert on real text, not raw keys
        var older = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium", version: "1.8.0") }, lastSeen: _ => "1.7.0");
        await older.LoadAsync();
        Assert.True(older.Browsers.Single().UpdateAvailable);
        Assert.Contains("1.7.0", older.Browsers.Single().StatusText);

        var same = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium", version: "1.8.0") }, lastSeen: _ => "1.8.0");
        await same.LoadAsync();
        Assert.False(same.Browsers.Single().UpdateAvailable);

        // Something NEWER than the release is not "out of date" — that is a developer running an unpacked
        // build, and nagging them would be wrong.
        var newer = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium", version: "1.8.0") }, lastSeen: _ => "1.9.0");
        await newer.LoadAsync();
        Assert.False(newer.Browsers.Single().UpdateAvailable);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_browser_that_never_called_is_not_flagged_for_an_update()
    {
        // Nothing is out of date when nothing is installed — that state is "not connected".
        var vm = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium", version: "1.8.0") }, lastSeen: _ => null);

        await vm.LoadAsync();

        Assert.False(vm.Browsers.Single().UpdateAvailable);
        Assert.False(vm.Browsers.Single().IsConnected);
    }

    // ---- installing ----

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_successful_install_records_the_path_and_clears_busy()
    {
        var vm = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium") });
        await vm.LoadAsync();

        await vm.InstallSelectedAsync();

        Assert.Equal("/data/extension/chrome", Assert.Single(vm.Targets).InstalledPath);
        Assert.False(vm.IsBusy);
        Assert.False(vm.HasError);
        Assert.Equal(1.0, vm.Progress);

        // And the command really is wired to that method — the dialog's button is the only way in.
        // Awaited, not slept on: the sleep version of this failed under full-suite load.
        var viaCommand = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium") });
        await viaCommand.LoadAsync();
        await viaCommand.InstallCommand.Execute();
        Assert.True(Assert.Single(viaCommand.Targets).IsUnpacked);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_failed_install_surfaces_the_reason_and_leaves_the_dialog_usable()
    {
        var vm = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium") },
            install: (_, _, _) => Task.FromResult(ExtensionInstallResult.Fail("could not be verified")));
        await vm.LoadAsync();

        await vm.InstallSelectedAsync();

        Assert.True(vm.HasError);
        Assert.Contains("verified", vm.ErrorMessage);
        Assert.False(vm.IsBusy);                            // a stuck spinner is its own bug
        Assert.False(Assert.Single(vm.Targets).IsUnpacked);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Installing_with_nothing_selected_asks_rather_than_failing()
    {
        var vm = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium") });
        await vm.LoadAsync();
        vm.Browsers.Single().IsSelected = false;

        await vm.InstallSelectedAsync();

        Assert.True(vm.HasNotice);
        Assert.False(vm.HasError);
        Assert.False(Assert.Single(vm.Targets).IsUnpacked);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_family_this_app_is_too_old_for_says_so_instead_of_installing()
    {
        Localizer.Instance.Load("en");   // these assert on real text, not raw keys
        // The catalog gate already dropped the entry, so the family has no build at all.
        var vm = Vm(new[] { Firefox }, Array.Empty<ExtensionCatalogEntry>());
        await vm.LoadAsync();

        await vm.InstallSelectedAsync();

        Assert.True(vm.HasError);
        Assert.DoesNotContain("Ext_", vm.ErrorMessage);     // the key resolved to real text
        Assert.False(Assert.Single(vm.Targets).IsUnpacked);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Only_the_selected_browsers_families_are_installed()
    {
        var installed = new List<string>();
        var vm = Vm(new[] { Chrome, Firefox }, new[] { Entry("chrome", "chromium"), Entry("firefox", "gecko") },
            install: (e, _, _) =>
            {
                installed.Add(e.Id);
                return Task.FromResult(ExtensionInstallResult.Ok($"/data/{e.Id}", e.Version));
            });
        await vm.LoadAsync();
        vm.Browsers.Single(b => b.Id == "firefox").IsSelected = false;

        await vm.InstallSelectedAsync();

        Assert.Equal(new[] { "chrome" }, installed);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Two_browsers_of_one_family_install_that_build_once()
    {
        var edge = new DetectedBrowser
        {
            Id = "edge", Name = "Microsoft Edge", Family = BrowserFamily.Chromium, ExecutablePath = "/usr/bin/microsoft-edge",
        };
        var installed = new List<string>();
        var vm = Vm(new[] { Chrome, edge }, new[] { Entry("chrome", "chromium") },
            install: (e, _, _) =>
            {
                installed.Add(e.Id);
                return Task.FromResult(ExtensionInstallResult.Ok($"/data/{e.Id}", e.Version));
            });
        await vm.LoadAsync();

        await vm.InstallSelectedAsync();

        // Chrome and Edge load the same build — downloading it twice would be pure waste.
        Assert.Equal(new[] { "chrome" }, installed);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_already_unpacked_build_is_reported_on_load()
    {
        var vm = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium") },
            installedPath: id => id == "chrome" ? "/data/extension/chrome" : null);

        await vm.LoadAsync();

        // Reopening the dialog must still show where the folder is: that path is what the user pastes
        // into their browser, and it is easy to lose.
        Assert.Equal("/data/extension/chrome", Assert.Single(vm.Targets).InstalledPath);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Refreshing_the_connection_state_flips_the_marker_without_reloading()
    {
        var reported = (string)null;
        var vm = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium", version: "1.8.0") }, lastSeen: _ => reported);
        await vm.LoadAsync();
        Assert.False(vm.Browsers.Single().IsConnected);

        reported = "1.8.0";
        vm.RefreshConnectionState();

        // This is how the tick appears while the dialog is open, without reopening anything.
        Assert.True(vm.Browsers.Single().IsConnected);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Detach_stops_the_language_subscription()
    {
        var vm = Vm();
        await vm.LoadAsync();

        vm.Detach();   // a dialog that leaks this keeps the VM alive for the life of the process
        vm.Detach();   // idempotent
    }
}
