using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.Tests.Plugins.Hls;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins;

/// <summary>
/// Reading the optional-plugin catalog off a release.
///
/// The parsing rules were already covered; what wasn't is the fetch around them, and that is where the
/// user-visible failures live. Every one of these paths ends in an EMPTY "More plugins" list, so a bug
/// here looks exactly like "there are no optional plugins" — silent, and indistinguishable from working
/// correctly. The service is deliberately failure-tolerant, which is precisely why each tolerated
/// failure needs to be shown to actually be tolerated rather than throwing into the Plugins page.
/// </summary>
public class PluginCatalogFetchTests : IDisposable
{
    public void Dispose() => PluginCatalogService.ReleasesUrlOverride = null;

    private static LoopbackServer Release(string releaseJson)
    {
        var server = new LoopbackServer();
        server.MapText("/releases/latest", releaseJson, "application/json");
        PluginCatalogService.ReleasesUrlOverride = server.Url("releases/latest");
        return server;
    }

    /// <summary>The normal path: the catalog manifest is an asset, and each entry resolves to its zip.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Entries_are_resolved_against_the_release_assets()
    {
        using var server = new LoopbackServer();
        server.MapText("/releases/latest", $$"""
        {
          "tag_name": "v9.9.9",
          "assets": [
            { "name": "plugins-catalog.json", "browser_download_url": "{{server.Url("catalog.json")}}" },
            { "name": "hls-plugin.zip",       "browser_download_url": "{{server.Url("hls.zip")}}" }
          ]
        }
        """, "application/json");
        server.MapText("/catalog.json", $$"""
        [
          { "id": "com.bezzad.hls", "name": "Streaming media", "version": "2.2.0",
            "assetName": "hls-plugin.zip", "sha256": "abc123" },
          { "id": "com.bezzad.absent", "name": "Not attached", "version": "1.0.0",
            "assetName": "missing.zip", "sha256": "def456" }
        ]
        """, "application/json");
        PluginCatalogService.ReleasesUrlOverride = server.Url("releases/latest");

        var catalog = await PluginCatalogService.FetchAsync(CancellationToken.None);

        var entry = Assert.Single(catalog);
        Assert.Equal("com.bezzad.hls", entry.Id);
        Assert.Equal(server.Url("hls.zip"), entry.AssetUrl);
        // An entry naming an asset the release does not carry is unusable — offering it would produce a
        // download that 404s at Add time.
        Assert.DoesNotContain(catalog, e => e.Id == "com.bezzad.absent");
    }

    /// <summary>Assets with no name or no download URL are noise, not entries.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Malformed_assets_are_skipped_rather_than_breaking_the_catalog()
    {
        using var server = new LoopbackServer();
        server.MapText("/releases/latest", $$"""
        {
          "assets": [
            { "name": "no-url.zip" },
            { "browser_download_url": "{{server.Url("no-name.zip")}}" },
            { "name": "plugins-catalog.json", "browser_download_url": "{{server.Url("catalog.json")}}" },
            { "name": "web-plugin.zip", "browser_download_url": "{{server.Url("web.zip")}}" }
          ]
        }
        """, "application/json");
        server.MapText("/catalog.json", $$"""
        [{ "id": "com.bezzad.website-zip", "assetName": "web-plugin.zip", "sha256": "aa" }]
        """, "application/json");
        PluginCatalogService.ReleasesUrlOverride = server.Url("releases/latest");

        var catalog = await PluginCatalogService.FetchAsync(CancellationToken.None);

        Assert.Equal("com.bezzad.website-zip", Assert.Single(catalog).Id);
    }

    /// <summary>A release that carries no catalog manifest simply offers nothing.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_release_without_a_catalog_manifest_offers_nothing()
    {
        using var server = Release("""
        { "tag_name": "v9.9.9", "assets": [ { "name": "Downloader-linux-x64.tar.gz",
          "browser_download_url": "https://host/app.tar.gz" } ] }
        """);

        Assert.Empty(await PluginCatalogService.FetchAsync(CancellationToken.None));
    }

    /// <summary>No assets array at all (a draft release, an unexpected payload shape).</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_release_payload_with_no_assets_offers_nothing()
    {
        using var server = Release("""{ "tag_name": "v9.9.9" }""");

        Assert.Empty(await PluginCatalogService.FetchAsync(CancellationToken.None));
    }

    /// <summary>Rate-limited or otherwise refused: degrade to an empty list, never an exception.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_refused_request_degrades_to_an_empty_catalog()
    {
        using var server = new LoopbackServer();
        server.MapStatus("/releases/latest", 403); // what a rate-limited GitHub answers
        PluginCatalogService.ReleasesUrlOverride = server.Url("releases/latest");

        Assert.Empty(await PluginCatalogService.FetchAsync(CancellationToken.None));
    }

    /// <summary>Offline, or a payload that isn't JSON at all.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_unreachable_or_unparsable_release_degrades_to_an_empty_catalog()
    {
        using (var server = Release("<html>not json</html>"))
            Assert.Empty(await PluginCatalogService.FetchAsync(CancellationToken.None));

        // 10.255.255.1 is the repo's unreachable address — the offline case.
        PluginCatalogService.ReleasesUrlOverride = "http://10.255.255.1/releases/latest";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        Assert.Empty(await PluginCatalogService.FetchAsync(cts.Token));
    }

    // ---- downloading an entry's asset --------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_asset_is_written_to_the_destination_it_is_given()
    {
        using var server = new LoopbackServer();
        server.MapBytes("/plugin.zip", new byte[] { 1, 2, 3, 4 }, "application/zip");
        var dest = Path.Combine(Directory.CreateTempSubdirectory("catalog-dl-").FullName, "nested", "plugin.zip");
        try
        {
            var info = new CatalogPluginInfo { Id = "x", AssetUrl = server.Url("plugin.zip") };

            Assert.True(await PluginCatalogService.DownloadAssetAsync(info, dest, CancellationToken.None));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(dest, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(dest)!)!, recursive: true);
        }
    }

    /// <summary>
    /// Every failure here has to come back as false so the Plugins page can say "could not download"
    /// instead of throwing out of a button handler.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_download_that_cannot_complete_reports_false_rather_than_throwing()
    {
        using var server = new LoopbackServer();
        server.MapStatus("/gone.zip", 404);
        var dir = Directory.CreateTempSubdirectory("catalog-dl-").FullName;
        try
        {
            Assert.False(await PluginCatalogService.DownloadAssetAsync(null, Path.Combine(dir, "a.zip"), TestContext.Current.CancellationToken));
            Assert.False(await PluginCatalogService.DownloadAssetAsync(
                new CatalogPluginInfo { Id = "x", AssetUrl = "" }, Path.Combine(dir, "a.zip"), TestContext.Current.CancellationToken));
            Assert.False(await PluginCatalogService.DownloadAssetAsync(
                new CatalogPluginInfo { Id = "x", AssetUrl = server.Url("gone.zip") }, Path.Combine(dir, "a.zip"), TestContext.Current.CancellationToken));

            // A destination that cannot be written (a directory in the file's place) must be survivable too.
            var occupied = Path.Combine(dir, "occupied.zip");
            Directory.CreateDirectory(occupied);
            server.MapBytes("/ok.zip", new byte[] { 9 }, "application/zip");
            Assert.False(await PluginCatalogService.DownloadAssetAsync(
                new CatalogPluginInfo { Id = "x", AssetUrl = server.Url("ok.zip") }, occupied, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
