using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.Tests.Plugins.Hls;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// Reading <c>extension-catalog.json</c> off a release.
///
/// Every failure here ends in the SAME user-visible state — the install dialog offering nothing — so a bug
/// is indistinguishable from "there is no build for your browser". The service is deliberately
/// failure-tolerant, which is exactly why each tolerated failure has to be shown to be tolerated rather
/// than throwing into the dialog.
/// </summary>
public class ExtensionCatalogServiceTests : IDisposable
{
    public void Dispose() => ExtensionCatalogService.ReleasesUrlOverride = null;

    private static readonly Version Old = new(2, 0, 0);
    private static readonly Version New = new(9, 9, 9);

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Entries_resolve_against_the_release_assets()
    {
        using var server = new LoopbackServer();
        server.MapText("/releases/latest", $$"""
        {
          "tag_name": "v9.9.9",
          "assets": [
            { "name": "extension-catalog.json",            "browser_download_url": "{{server.Url("catalog.json")}}" },
            { "name": "downloader-extension-chrome.zip",   "browser_download_url": "{{server.Url("chrome.zip")}}" },
            { "name": "downloader-extension-firefox.zip",  "browser_download_url": "{{server.Url("firefox.zip")}}" }
          ]
        }
        """, "application/json");
        server.MapText("/catalog.json", """
        [
          { "id": "chrome",  "family": "chromium", "name": "Chrome, Edge", "version": "1.7.0",
            "assetName": "downloader-extension-chrome.zip",  "sha256": "aa", "minAppVersion": "1.0.0", "storeUrl": null },
          { "id": "firefox", "family": "gecko",    "name": "Firefox",      "version": "1.7.0",
            "assetName": "downloader-extension-firefox.zip", "sha256": "bb", "minAppVersion": "1.0.0",
            "storeUrl": "https://addons.example/downloader" }
        ]
        """, "application/json");
        ExtensionCatalogService.ReleasesUrlOverride = server.Url("releases/latest");

        var catalog = await ExtensionCatalogService.FetchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, catalog.Count);
        var chrome = catalog.Single(e => e.Id == "chrome");
        Assert.Equal(server.Url("chrome.zip"), chrome.AssetUrl);
        Assert.Equal("1.7.0", chrome.Version);
        Assert.False(chrome.HasStore);                      // null storeUrl → the manual path
        Assert.True(catalog.Single(e => e.Id == "firefox").HasStore);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_release_without_the_catalog_asset_offers_nothing()
    {
        using var server = new LoopbackServer();
        server.MapText("/releases/latest", $$"""
        { "assets": [ { "name": "Downloader-linux-x64.tar.gz",
                        "browser_download_url": "{{server.Url("app.tar.gz")}}" } ] }
        """, "application/json");
        ExtensionCatalogService.ReleasesUrlOverride = server.Url("releases/latest");

        Assert.Empty(await ExtensionCatalogService.FetchAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Malformed_catalog_json_offers_nothing_rather_than_throwing()
    {
        using var server = new LoopbackServer();
        server.MapText("/releases/latest", $$"""
        { "assets": [ { "name": "extension-catalog.json",
                        "browser_download_url": "{{server.Url("catalog.json")}}" } ] }
        """, "application/json");
        server.MapText("/catalog.json", "{ not json at all", "application/json");
        ExtensionCatalogService.ReleasesUrlOverride = server.Url("releases/latest");

        Assert.Empty(await ExtensionCatalogService.FetchAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = TestTimeouts.SlowMs)]
    public async Task An_unreachable_release_offers_nothing_rather_than_throwing()
    {
        // The repo's unreachable address — never a .invalid hostname, whose DNS lookup stalls the suite.
        ExtensionCatalogService.ReleasesUrlOverride = "http://10.255.255.1/releases/latest";

        Assert.Empty(await ExtensionCatalogService.FetchAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_release_that_errors_offers_nothing()
    {
        using var server = new LoopbackServer();
        server.MapStatus("/releases/latest", 503);
        ExtensionCatalogService.ReleasesUrlOverride = server.Url("releases/latest");

        Assert.Empty(await ExtensionCatalogService.FetchAsync(TestContext.Current.CancellationToken));
    }

    // ---- ParseCatalog: the gating rules, pure ----

    private static Dictionary<string, string> Assets(params string[] names)
        => names.ToDictionary(n => n, n => $"https://example.test/{n}", StringComparer.OrdinalIgnoreCase);

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_entry_naming_an_asset_the_release_lacks_is_dropped()
    {
        var parsed = ExtensionCatalogService.ParseCatalog("""
        [ { "id": "chrome", "assetName": "absent.zip", "sha256": "aa", "minAppVersion": "1.0.0" } ]
        """, Assets("downloader-extension-chrome.zip"), New);

        Assert.Empty(parsed);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_entry_needing_a_newer_app_is_not_offered()
    {
        const string json = """
        [ { "id": "chrome", "assetName": "x.zip", "sha256": "aa", "minAppVersion": "5.0.0" } ]
        """;

        // Offering a build this app's local API cannot serve would produce a broken extension, so the
        // dialog must not list it at all.
        Assert.Empty(ExtensionCatalogService.ParseCatalog(json, Assets("x.zip"), Old));
        Assert.Single(ExtensionCatalogService.ParseCatalog(json, Assets("x.zip"), New));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_entry_without_a_checksum_is_dropped()
    {
        // No checksum means nothing to verify against, and installing unverified files is the one thing
        // this flow must never do.
        Assert.Empty(ExtensionCatalogService.ParseCatalog("""
        [ { "id": "chrome", "assetName": "x.zip", "minAppVersion": "1.0.0" } ]
        """, Assets("x.zip"), New));
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("""[ { "assetName": "x.zip", "sha256": "aa" } ]""")]                 // no id
    [InlineData("""[ { "id": "chrome", "sha256": "aa" } ]""")]                       // no asset
    [InlineData("""{ "id": "chrome" }""")]                                          // not an array
    [InlineData("[]")]
    public void Unusable_catalog_shapes_yield_nothing(string json)
        => Assert.Empty(ExtensionCatalogService.ParseCatalog(json, Assets("x.zip"), New));

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Missing_optional_fields_fall_back_to_safe_defaults()
    {
        var e = Assert.Single(ExtensionCatalogService.ParseCatalog("""
        [ { "id": "chrome", "assetName": "x.zip", "sha256": "aa" } ]
        """, Assets("x.zip"), New));

        Assert.Equal("chromium", e.Family);   // the common case, not a crash
        Assert.Equal("chrome", e.Name);       // falls back to the id rather than showing blank
        Assert.Equal("", e.Version);
        Assert.False(e.HasStore);             // no storeUrl → manual path, never a dead link
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("1.8.0", "1.7.0", true)]
    [InlineData("1.7.0", "1.7.0", false)]
    [InlineData("1.7.0", "1.8.0", false)]
    [InlineData("1.7.0", "", false)]
    [InlineData("", "1.7.0", false)]
    public void IsNewer_only_reports_a_strictly_newer_published_version(string published, string reported, bool expected)
        => Assert.Equal(expected, ExtensionCatalogService.IsNewer(published, reported));
}

/// <summary>
/// The startup "your extension is out of date" decision.
///
/// Its job is to be quiet. A check like this only earns its place if it never cries wolf, so most of
/// these tests are about the cases where it must say nothing.
/// </summary>
public class ExtensionUpdateWarningTests
{
    private static ExtensionCatalogEntry Build(string id, string version)
        => new() { Id = id, Family = id, Version = version, AssetName = $"{id}.zip", AssetUrl = "u", Sha256 = "s" };

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_older_reported_version_is_warned_about()
    {
        var warn = ExtensionCatalogService.ShouldWarnAboutExtension(
            new[] { "1.7.0" }, new[] { Build("chrome", "1.8.0") }, null, out var reported, out var available);

        Assert.True(warn);
        Assert.Equal("1.7.0", reported);
        Assert.Equal("1.8.0", available);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Nothing_reported_is_never_warned_about()
    {
        // An extension that has never called is not out of date — it is not installed. Nagging here
        // would be pure noise for someone who does not use the extension at all.
        Assert.False(ExtensionCatalogService.ShouldWarnAboutExtension(
            Array.Empty<string>(), new[] { Build("chrome", "1.8.0") }, null, out _, out _));
        Assert.False(ExtensionCatalogService.ShouldWarnAboutExtension(
            null, new[] { Build("chrome", "1.8.0") }, null, out _, out _));
        Assert.False(ExtensionCatalogService.ShouldWarnAboutExtension(
            new string[] { null }, new[] { Build("chrome", "1.8.0") }, null, out _, out _));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void With_nothing_to_compare_against_nothing_is_warned_about()
    {
        // Offline AND no bundled copy must be silent, not "you are out of date". (A bundled copy IS
        // something to compare against — see The_apps_own_bundled_copy_is_enough_to_warn.)
        Assert.False(ExtensionCatalogService.ShouldWarnAboutExtension(
            new[] { "1.7.0" }, Array.Empty<ExtensionCatalogEntry>(), null, out _, out _));
        Assert.False(ExtensionCatalogService.ShouldWarnAboutExtension(new[] { "1.7.0" }, null, null, out _, out _));
        Assert.False(ExtensionCatalogService.ShouldWarnAboutExtension(
            new[] { "1.7.0" }, new[] { Build("chrome", "") }, null, out _, out _));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_current_or_newer_extension_is_never_warned_about()
    {
        Assert.False(ExtensionCatalogService.ShouldWarnAboutExtension(
            new[] { "1.8.0" }, new[] { Build("chrome", "1.8.0") }, null, out _, out _));
        // A developer running an unpacked build ahead of the release must not be nagged.
        Assert.False(ExtensionCatalogService.ShouldWarnAboutExtension(
            new[] { "1.9.0" }, new[] { Build("chrome", "1.8.0") }, null, out _, out _));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_apps_own_bundled_copy_is_enough_to_warn()
    {
        // The case that was silently broken: NO published release carries an extension catalog yet, so a
        // check that only consulted the catalog answered "no" on every machine — a Chrome running v1.11.0
        // against a newer copy inside the app was never told (reported 2026-09-04).
        var warn = ExtensionCatalogService.ShouldWarnAboutExtension(
            new[] { "1.11.0" }, Array.Empty<ExtensionCatalogEntry>(), "1.13.0",
            out var reported, out var available);

        Assert.True(warn);
        Assert.Equal("1.11.0", reported);
        Assert.Equal("1.13.0", available);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_newer_of_the_catalog_and_the_bundled_copy_is_the_one_offered()
    {
        // Right after an app update the copy inside the app is routinely ahead of the last release's
        // catalog. Offering the catalog's older build there would be a downgrade.
        Assert.True(ExtensionCatalogService.ShouldWarnAboutExtension(
            new[] { "1.9.0" }, new[] { Build("chrome", "1.10.0") }, "1.13.0", out _, out var available));
        Assert.Equal("1.13.0", available);

        // …and the other way round: a catalog ahead of the bundled copy wins.
        Assert.True(ExtensionCatalogService.ShouldWarnAboutExtension(
            new[] { "1.9.0" }, new[] { Build("chrome", "1.14.0") }, "1.13.0", out _, out available));
        Assert.Equal("1.14.0", available);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_browser_running_exactly_the_bundled_copy_is_left_alone()
    {
        Assert.False(ExtensionCatalogService.ShouldWarnAboutExtension(
            new[] { "1.13.0" }, Array.Empty<ExtensionCatalogEntry>(), "1.13.0", out _, out _));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void With_two_browsers_the_oldest_one_is_named()
    {
        // The point of naming a version is to tell the user which install needs attention.
        var warn = ExtensionCatalogService.ShouldWarnAboutExtension(
            new[] { "1.8.0", "1.6.0", "1.7.0" }, new[] { Build("chrome", "1.8.0") }, null, out var reported, out _);

        Assert.True(warn);
        Assert.Equal("1.6.0", reported);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_newest_published_build_is_the_one_compared_against()
    {
        Assert.Equal("2.0.0", ExtensionCatalogService.NewestVersion(
            new[] { Build("chrome", "1.8.0"), Build("firefox", "2.0.0") }));
        Assert.Null(ExtensionCatalogService.NewestVersion(Array.Empty<ExtensionCatalogEntry>()));
        Assert.Null(ExtensionCatalogService.NewestVersion(null));
        Assert.Null(ExtensionCatalogService.NewestVersion(new ExtensionCatalogEntry[] { null }));
    }
}
