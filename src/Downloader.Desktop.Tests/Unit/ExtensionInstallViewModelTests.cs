using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using ReactiveUI;
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

    /// <summary>A supported browser that detection could NOT confirm on this machine — no path found,
    /// which is exactly what "not installed" means (DetectedBrowser.IsInstalled derives from it).</summary>
    private static DetectedBrowser NotInstalled(string id, string name, BrowserFamily family) => new()
    {
        Id = id, Name = name, Family = family, ExecutablePath = null,
    };

    private static ExtensionCatalogEntry Entry(string id, string family, string version = "1.8.0", string storeUrl = null)
        => new()
        {
            Id = id, Family = family, Name = id, Version = version,
            AssetName = $"{id}.zip", AssetUrl = $"https://example.test/{id}.zip",
            Sha256 = new string('a', 64), MinAppVersion = "1.0.0", StoreUrl = storeUrl,
        };

    /// <summary>
    /// A VM with every OS/network edge stubbed; `install` defaults to succeeding.
    ///
    /// <para><paramref name="bundledVersion"/> defaults to a version older than anything else here, so a
    /// test that is about the CATALOG is not silently steered by whatever copy this app happens to ship
    /// (the app's own build is a real input to "what is available" — see the tests at the end).</para>
    /// </summary>
    private static ExtensionInstallViewModel Vm(
        IEnumerable<DetectedBrowser> browsers = null,
        IEnumerable<ExtensionCatalogEntry> catalog = null,
        Func<ExtensionCatalogEntry, IProgress<double>, CancellationToken, Task<ExtensionInstallResult>> install = null,
        Func<string, string> lastSeen = null,
        Func<string, InstalledCopy> readInstalled = null,
        string bundledVersion = "0.0.1",
        Func<string, bool, ExtensionInstallResult> installBundled = null)
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
            readInstalled: readInstalled ?? (_ => null),
            installBundled: installBundled ?? ((id, _) =>
                ExtensionInstallResult.Ok($"/data/extension/{id}", bundledVersion)),
            bundledVersion: () => bundledVersion);

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

    /// <summary>
    /// The reported bug: with only Firefox detected, the dialog offered ONLY the Gecko folder — so a user
    /// running Chrome (which a snap-confined app cannot see at all) had no folder to install into. Both
    /// families are always offered, whatever detection found.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Both_families_get_a_folder_even_when_only_one_browser_was_detected()
    {
        var vm = Vm(new[] { Firefox }, new[] { Entry("chrome", "chromium"), Entry("firefox", "gecko") });

        await vm.LoadAsync();

        Assert.Equal(new[] { BrowserFamily.Chromium, BrowserFamily.Gecko }, vm.Targets.Select(t => t.Family));
        Assert.True(vm.CanInstall);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Both_families_get_a_folder_even_when_NOTHING_was_detected()
    {
        // What a strictly confined snap sees: no browser at all. The extension must still be installable.
        var vm = Vm(Array.Empty<DetectedBrowser>(), Array.Empty<ExtensionCatalogEntry>());

        await vm.LoadAsync();

        Assert.Equal(new[] { BrowserFamily.Chromium, BrowserFamily.Gecko }, vm.Targets.Select(t => t.Family));
        Assert.True(vm.CanInstall);
        Assert.False(vm.HasError);
    }

    /// <summary>
    /// EVERY supported browser is listed, with the undetected ones present but not pre-ticked — Chrome was
    /// missing from the list entirely on the machine that reported this.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_undetected_browser_is_still_listed_just_not_preselected()
    {
        var vm = Vm(new[] { Firefox, NotInstalled("chrome", "Google Chrome", BrowserFamily.Chromium) });

        await vm.LoadAsync();

        Assert.Equal(new[] { "Mozilla Firefox", "Google Chrome" }, vm.Browsers.Select(b => b.Name));
        Assert.True(vm.Browsers.Single(b => b.Id == "firefox").IsSelected);
        var chrome = vm.Browsers.Single(b => b.Id == "chrome");
        Assert.False(chrome.IsInstalled);
        Assert.False(chrome.IsSelected);
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

    /// <summary>
    /// No catalog is the NORMAL state today: every release published before this feature carries no
    /// extension-catalog.json, so an installer that gave up here would be dead on arrival — which is
    /// exactly what the author hit. The app's own copy is installed instead.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_empty_catalog_falls_back_to_the_copy_bundled_with_the_app()
    {
        var vm = Vm(new[] { Chrome }, Array.Empty<ExtensionCatalogEntry>());

        await vm.LoadAsync();

        Assert.True(vm.HasNotice);        // it says which copy it is using
        Assert.False(vm.HasError);        // …but this is not a failure
        Assert.True(vm.CanInstall);
        Assert.All(vm.Targets, t =>
        {
            Assert.True(t.HasBuild);
            Assert.True(t.IsBundled);
        });
    }

    // ---- there is no store path ----

    /// <summary>
    /// The dialog has no "open the store page" button, and a catalog that names a listing does not change
    /// what the dialog does: the extension ships INSIDE the app, so the files-and-folder path is the only
    /// one — and the old button did nothing anyway, because nothing is published.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_catalog_listing_url_does_not_replace_the_folder_path()
    {
        var vm = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium", storeUrl: "https://store.example/x") });

        await vm.LoadAsync();

        Assert.True(vm.CanInstall);
        await vm.InstallSelectedAsync();
        Assert.True(vm.Targets.Single(t => t.Family == BrowserFamily.Chromium).IsUnpacked);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_browser_row_exposes_no_store_command()
    {
        // Compile-time would catch a re-added property; this pins the intent for a reader too.
        Assert.DoesNotContain("OpenStore",
            typeof(ExtensionBrowserRow).GetProperties().Select(p => p.Name));
        Assert.DoesNotContain("UseStore",
            typeof(ExtensionTargetRow).GetProperties().Select(p => p.Name));
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

        Assert.Contains("restart", vm.Targets.Single(t => t.Family == BrowserFamily.Gecko).Limitations,
            StringComparison.OrdinalIgnoreCase);
    }

    // ---- connected / out of date ----

    /// <summary>The show-don't-claim rule, in one test.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Unpacking_alone_never_claims_the_extension_is_connected()
    {
        var vm = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium") }, lastSeen: _ => null);
        await vm.LoadAsync();

        await vm.InstallSelectedAsync();

        Assert.True(vm.Targets.Single(t => t.Family == BrowserFamily.Chromium).IsUnpacked);  // files are there…
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

    // ---- which version is on disk ----

    /// <summary>
    /// The reported complaint: the dialog could not say which version was installed. The per-browser tick
    /// needs the extension to have CALLED the app, which it has not on a fresh install — so the version of
    /// the files themselves is read from the install record and shown per family.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_version_on_disk_is_shown_without_the_extension_having_called()
    {
        Localizer.Instance.Load("en");
        var vm = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium", version: "1.12.0") },
            lastSeen: _ => null,   // nothing has ever contacted the app
            readInstalled: id => id == "chrome" ? new InstalledCopy("/data/extension/chrome", "1.11.0") : null);

        await vm.LoadAsync();

        var chromium = vm.Targets.Single(t => t.Family == BrowserFamily.Chromium);
        Assert.True(chromium.HasInstalledVersion);
        Assert.Equal("1.11.0", chromium.InstalledVersion);
        Assert.Contains("1.11.0", chromium.InstalledVersionText);
        Assert.DoesNotContain("Ext_InstalledVersion", chromium.InstalledVersionText);   // a real string
        Assert.Equal("1.12.0", chromium.AvailableVersion);                              // what Install would put there

        // A family with nothing unpacked says nothing rather than "v".
        Assert.False(vm.Targets.Single(t => t.Family == BrowserFamily.Gecko).HasInstalledVersion);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Installing_updates_the_version_shown_on_disk()
    {
        var vm = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium", version: "1.12.0") },
            readInstalled: _ => null);
        await vm.LoadAsync();
        Assert.False(vm.Targets.Single(t => t.Family == BrowserFamily.Chromium).HasInstalledVersion);

        await vm.InstallSelectedAsync();

        Assert.Equal("1.12.0", vm.Targets.Single(t => t.Family == BrowserFamily.Chromium).InstalledVersion);
    }

    /// <summary>
    /// The connected version is matched to a row by the browser id the extension reports, so every id the
    /// extension can send has to be a browser the app lists — a Brave that reported "chrome" had its
    /// version attributed to Chrome and its own row read "not added yet" forever (fixed in extension
    /// 1.12.0, which reports the real browser; the label vocabulary is pinned by
    /// BrowserDetectorTests.Every_browser_the_extension_can_report_is_a_browser_the_app_lists).
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_forks_own_row_shows_its_version_when_it_reports_its_own_id()
    {
        Localizer.Instance.Load("en");
        var brave = new DetectedBrowser
        {
            Id = "brave", Name = "Brave", Family = BrowserFamily.Chromium,
            ExecutablePath = "/usr/bin/brave-browser",
        };
        var vm = Vm(new[] { Chrome, brave }, new[] { Entry("chrome", "chromium", version: "1.12.0") },
            lastSeen: id => id == "brave" ? "1.12.0" : null);

        await vm.LoadAsync();

        Assert.True(vm.Browsers.Single(b => b.Id == "brave").IsConnected);
        Assert.False(vm.Browsers.Single(b => b.Id == "chrome").IsConnected);   // not its version to claim
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_undetected_browser_says_so_instead_of_not_added_yet()
    {
        Localizer.Instance.Load("en");
        var vm = Vm(new[] { NotInstalled("chrome", "Google Chrome", BrowserFamily.Chromium) });

        await vm.LoadAsync();

        var status = vm.Browsers.Single().StatusText;
        Assert.False(string.IsNullOrWhiteSpace(status));
        Assert.DoesNotContain("Ext_Not", status);          // a real string, not a raw key
    }

    // ---- installing ----

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_successful_install_records_the_path_and_clears_busy()
    {
        var vm = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium") });
        await vm.LoadAsync();

        await vm.InstallSelectedAsync();

        Assert.Equal("/data/extension/chrome",
            vm.Targets.Single(t => t.Family == BrowserFamily.Chromium).InstalledPath);
        Assert.False(vm.IsBusy);
        Assert.False(vm.HasError);
        // Deliberately NOT asserting vm.Progress here. The VM reports through System.Progress<double>,
        // which POSTS its callback to the captured SynchronizationContext instead of running it inline —
        // so the value may or may not have arrived by the time this line runs, which is exactly the race
        // that made this test fail in a full run and pass on its own. Progress<T> is the right choice for
        // the VM (it keeps the bound property update off the engine's background thread), so the contract
        // is asserted where it is deterministic: ExtensionInstallServiceTests
        // .Reporting_progress_reaches_one_hundred_percent, against the real service.

        // And the command really is wired to that method — the dialog's button is the only way in.
        //
        // A ReactiveCommand delivers its result on RxApp.MainThreadScheduler, which in a plain [Fact] is
        // whatever the last test left there: nothing pumps it, so awaiting Execute() hangs until the
        // timeout. (Sleeping instead was worse — that failed under full-suite load.) Pin the scheduler for
        // the duration so the execution is synchronous and the assertion is about wiring, not timing.
        var previousScheduler = RxApp.MainThreadScheduler;
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        try
        {
            var viaCommand = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium") });
            await viaCommand.LoadAsync();
            await viaCommand.InstallCommand.Execute();
            Assert.True(viaCommand.Targets.Single(t => t.Family == BrowserFamily.Chromium).IsUnpacked);
        }
        finally
        {
            // Process-wide: leaking it silently changes a LATER test, not this one.
            RxApp.MainThreadScheduler = previousScheduler;
        }
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
        Assert.All(vm.Targets, t => Assert.False(t.IsUnpacked));
    }

    /// <summary>
    /// Nothing ticked installs BOTH families rather than refusing. On a machine where detection confirms
    /// nothing, no row is pre-ticked — and a dialog whose only button then said "pick a browser" was a
    /// dead end.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Installing_with_nothing_selected_installs_every_family()
    {
        var vm = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium"), Entry("firefox", "gecko") });
        await vm.LoadAsync();
        foreach (var row in vm.Browsers)
            row.IsSelected = false;

        await vm.InstallSelectedAsync();

        Assert.False(vm.HasError);
        Assert.All(vm.Targets, t => Assert.True(t.IsUnpacked));
    }

    /// <summary>
    /// A family the catalog cannot serve — unreachable, or gated out by minAppVersion — installs the
    /// bundled copy, which by construction matches the running app. There is no longer a state where the
    /// user is told to update the app before they can get the extension.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_family_the_catalog_cannot_serve_installs_the_bundled_copy()
    {
        var installedBundled = new List<(string Target, bool Gecko)>();
        var vm = new ExtensionInstallViewModel(
            detect: () => new[] { Firefox },
            fetchCatalog: _ => Task.FromResult<IReadOnlyList<ExtensionCatalogEntry>>(Array.Empty<ExtensionCatalogEntry>()),
            install: (_, _, _) => throw new InvalidOperationException("the catalog path must not be used here"),
            lastSeenVersion: _ => null,
            readInstalled: _ => null,
            installBundled: (target, gecko) =>
            {
                installedBundled.Add((target, gecko));
                return ExtensionInstallResult.Ok("/data/extension/" + target, "1.8.0");
            },
            bundledVersion: () => "1.8.0");
        await vm.LoadAsync();

        await vm.InstallSelectedAsync();

        Assert.False(vm.HasError);
        Assert.Equal(("firefox", true), Assert.Single(installedBundled));   // and with the gecko manifest
        Assert.True(vm.Targets.Single(t => t.Family == BrowserFamily.Gecko).IsUnpacked);
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
            readInstalled: id => id == "chrome" ? new InstalledCopy("/data/extension/chrome", "1.8.0") : null);

        await vm.LoadAsync();

        // Reopening the dialog must still show where the folder is: that path is what the user pastes
        // into their browser, and it is easy to lose.
        Assert.Equal("/data/extension/chrome",
            vm.Targets.Single(t => t.Family == BrowserFamily.Chromium).InstalledPath);
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

    // ---- is what is on disk out of date, and can it be updated in place? ----

    /// <summary>
    /// The reported complaint (2026-09-04): the dialog showed "Files installed: v1.11.0" next to the
    /// available version and left the user to work out which was which — so a Chrome running a stale copy
    /// looked exactly like a healthy one. The comparison is now drawn for them.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Files_older_than_the_build_on_offer_are_called_out_as_out_of_date()
    {
        Localizer.Instance.Load("en");   // asserts on real text, not raw keys
        var vm = Vm(new[] { Chrome }, Array.Empty<ExtensionCatalogEntry>(),
            readInstalled: _ => new InstalledCopy("/data/extension/chrome", "1.11.0"),
            bundledVersion: "1.13.0");

        await vm.LoadAsync();

        var target = vm.Targets.Single(t => t.Family == BrowserFamily.Chromium);
        Assert.True(target.UpdateAvailable);
        Assert.Equal("1.13.0", target.AvailableVersion);
        Assert.Contains("1.13.0", target.UpdateText);
        Assert.True(vm.AnyUpdateAvailable);
        // The button has to say so too — "Get the files" on a machine that already has them reads as
        // "nothing to do here", which is how the stale copy went unnoticed.
        Assert.Equal(Localizer.Instance["Ext_Update"], vm.InstallButtonText);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Files_that_match_the_build_on_offer_say_so_instead()
    {
        Localizer.Instance.Load("en");
        var vm = Vm(new[] { Chrome }, Array.Empty<ExtensionCatalogEntry>(),
            readInstalled: _ => new InstalledCopy("/data/extension/chrome", "1.13.0"),
            bundledVersion: "1.13.0");

        await vm.LoadAsync();

        var target = vm.Targets.Single(t => t.Family == BrowserFamily.Chromium);
        Assert.False(target.UpdateAvailable);
        Assert.Equal(Localizer.Instance["Ext_FilesUpToDate"], target.UpdateText);
        Assert.False(vm.AnyUpdateAvailable);
        Assert.Equal(Localizer.Instance["Ext_Install"], vm.InstallButtonText);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Nothing_installed_is_not_out_of_date()
    {
        // "Not installed" and "out of date" are different states, and only one of them is a problem.
        var vm = Vm(new[] { Chrome }, Array.Empty<ExtensionCatalogEntry>(), bundledVersion: "1.13.0");

        await vm.LoadAsync();

        Assert.All(vm.Targets, t => Assert.False(t.UpdateAvailable));
        Assert.All(vm.Targets, t => Assert.Null(t.UpdateText));
        Assert.False(vm.AnyUpdateAvailable);
    }

    /// <summary>
    /// The user's second question: once installed from here, can a new version just replace the folder's
    /// contents? Yes — and the path staying identical is the whole point. A browser derives an unpacked
    /// extension's identity from its absolute folder path, so installing an update anywhere else would
    /// read as a DIFFERENT extension with an empty settings store.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_update_rewrites_the_same_folder_and_clears_the_out_of_date_state()
    {
        Localizer.Instance.Load("en");
        var installedInto = new List<string>();
        var vm = Vm(new[] { Chrome }, Array.Empty<ExtensionCatalogEntry>(),
            // Only Chrome has files on disk here — the Gecko folder is empty, which is the ordinary
            // state for someone who installed into one browser.
            readInstalled: id => id == "chrome" ? new InstalledCopy("/data/extension/chrome", "1.11.0") : null,
            bundledVersion: "1.13.0",
            installBundled: (id, _) =>
            {
                installedInto.Add(id);
                return ExtensionInstallResult.Ok($"/data/extension/{id}", "1.13.0");
            });

        await vm.LoadAsync();
        var target = vm.Targets.Single(t => t.Family == BrowserFamily.Chromium);
        var pathBefore = target.InstalledPath;

        await vm.InstallSelectedAsync();

        Assert.Equal(pathBefore, target.InstalledPath);      // same folder — the browser keeps its extension
        Assert.Equal("1.13.0", target.InstalledVersion);
        Assert.False(target.UpdateAvailable);
        Assert.False(vm.AnyUpdateAvailable);
        Assert.Contains("chrome", installedInto);
        // Writing the files is not the same as the browser reading them, and the user has to be told:
        // an unpacked extension stays loaded until it is reloaded or the browser restarts.
        Assert.True(vm.HasNotice);
        Assert.Equal(Localizer.Instance["Ext_ReloadAfterUpdate"], vm.Notice);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_FIRST_install_does_not_tell_the_user_to_reload_anything()
    {
        // There is nothing loaded to reload — the reload notice belongs to an update, or it is noise.
        var vm = Vm(new[] { Chrome }, Array.Empty<ExtensionCatalogEntry>(), bundledVersion: "1.13.0");

        await vm.LoadAsync();
        await vm.InstallSelectedAsync();

        Assert.False(vm.HasNotice);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_bundled_copy_is_installed_when_it_is_NEWER_than_the_catalog()
    {
        // Right after an app update the copy inside the app is routinely ahead of the last release's
        // catalog; installing the catalog's build there would be a downgrade dressed up as an install.
        var bundledUsed = false;
        var vm = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium", version: "1.10.0") },
            bundledVersion: "1.13.0",
            installBundled: (id, _) => { bundledUsed = true; return ExtensionInstallResult.Ok($"/data/extension/{id}", "1.13.0"); });

        await vm.LoadAsync();
        var target = vm.Targets.Single(t => t.Family == BrowserFamily.Chromium);
        Assert.True(target.IsBundled);
        Assert.Equal("1.13.0", target.AvailableVersion);

        await vm.InstallSelectedAsync();
        Assert.True(bundledUsed);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_catalog_newer_than_the_bundled_copy_still_wins()
    {
        var vm = Vm(new[] { Chrome }, new[] { Entry("chrome", "chromium", version: "1.14.0") },
            bundledVersion: "1.13.0");

        await vm.LoadAsync();

        var target = vm.Targets.Single(t => t.Family == BrowserFamily.Chromium);
        Assert.False(target.IsBundled);
        Assert.Equal("1.14.0", target.AvailableVersion);
    }

    /// <summary>
    /// A browser's own reported version is compared against the app's bundled copy too. With the catalog
    /// alone this could never fire, because no published release carries an extension catalog yet — which
    /// is why a Chrome sitting on v1.11.0 was never told anything was newer.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_connected_browser_is_told_it_is_behind_the_apps_own_copy()
    {
        Localizer.Instance.Load("en");
        var vm = Vm(new[] { Chrome }, Array.Empty<ExtensionCatalogEntry>(),
            lastSeen: _ => "1.11.0", bundledVersion: "1.13.0");

        await vm.LoadAsync();

        var row = vm.Browsers.Single();
        Assert.True(row.UpdateAvailable);
        Assert.Equal("1.13.0", row.AvailableVersion);
        Assert.Contains("1.13.0", row.StatusText);
    }
}
