using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins;

/// <summary>
/// Catalog parsing, version comparison, and the verify-before-load install gate for optional plugins
/// (consolidate-official-plugins change). Network-free: install tests build a real zip from the staged
/// sample plugin and install into a TEMP root so the user's real plugins folder is never touched.
/// </summary>
public class PluginCatalogTests
{
    private static string SamplePluginDir =>
        Path.Combine(System.AppContext.BaseDirectory, "plugins-sample");

    /// <summary>Zip the staged sample plugin (dll + deps.json) into a temp zip; return its path.</summary>
    private static string BuildSampleZip()
    {
        Assert.True(Directory.Exists(SamplePluginDir), "sample plugin was not staged — check the test csproj target");
        var stage = Directory.CreateTempSubdirectory("hls-zip-src-").FullName;
        foreach (var f in Directory.GetFiles(SamplePluginDir, "Downloader.Desktop.Plugins.GitHub.*"))
            File.Copy(f, Path.Combine(stage, Path.GetFileName(f)), overwrite: true);
        var zip = Path.Combine(Path.GetTempPath(), $"sample-plugin-{System.Guid.NewGuid():N}.zip");
        ZipFile.CreateFromDirectory(stage, zip);
        return zip;
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Install_from_zip_with_matching_checksum_loads_the_plugin()
    {
        var zip = BuildSampleZip();
        var sha = await PluginManager.ComputeSha256Async(zip, TestContext.Current.CancellationToken);
        var root = Directory.CreateTempSubdirectory("plugins-root-").FullName;

        var pm = new PluginManager();
        var result = await pm.InstallFromZipAsync(zip, sha, "com.bezzad.github-releases", root, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Plugin);
        Assert.Equal("com.bezzad.github-releases", result.Plugin.Id);
        Assert.False(result.Plugin.IsBuiltIn); // installed plugins are removable, not built-in
        Assert.Contains(pm.Plugins, p => p.Id == "com.bezzad.github-releases");
        // extracted into the given root, not the real PluginsRoot
        Assert.True(File.Exists(Path.Combine(root, "com.bezzad.github-releases", "Downloader.Desktop.Plugins.GitHub.dll")));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Install_from_zip_with_wrong_checksum_does_not_extract_or_load()
    {
        var zip = BuildSampleZip();
        var root = Directory.CreateTempSubdirectory("plugins-root-").FullName;

        var pm = new PluginManager();
        var result = await pm.InstallFromZipAsync(zip, "deadbeef", "com.bezzad.github-releases", root, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Empty(pm.Plugins);                                   // nothing loaded
        Assert.Empty(Directory.GetFileSystemEntries(root));         // nothing extracted — folder untouched
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void IsNewer_only_true_when_catalog_version_is_strictly_greater()
    {
        Assert.True(PluginCatalogService.IsNewer("1.2.0", "1.1.2"));
        Assert.True(PluginCatalogService.IsNewer("2.0.0", "1.9.9"));
        Assert.False(PluginCatalogService.IsNewer("1.1.2", "1.1.2")); // same
        Assert.False(PluginCatalogService.IsNewer("1.0.0", "1.1.2")); // older
        Assert.False(PluginCatalogService.IsNewer("", "1.1.2"));      // unparsable
        Assert.False(PluginCatalogService.IsNewer("1.2.0", ""));      // unparsable installed
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void ParseCatalog_maps_entries_and_resolves_asset_urls()
    {
        const string json = """
        [
          { "id": "com.bezzad.hls", "name": "HLS", "description": "d", "version": "1.1.2",
            "assetName": "Downloader.Desktop.Plugins.Hls.zip", "sha256": "abc123", "minAppVersion": "1.7.0" },
          { "id": "com.x.missing", "name": "Missing", "version": "1.0.0",
            "assetName": "not-on-release.zip", "sha256": "def" }
        ]
        """;
        var urls = new Dictionary<string, string>
        {
            ["Downloader.Desktop.Plugins.Hls.zip"] = "https://example.com/hls.zip",
        };

        var list = PluginCatalogService.ParseCatalog(json, urls);

        // The entry whose asset isn't attached to the release is dropped.
        var e = Assert.Single(list);
        Assert.Equal("com.bezzad.hls", e.Id);
        Assert.Equal("HLS", e.Name);
        Assert.Equal("1.1.2", e.Version);
        Assert.Equal("abc123", e.Sha256);
        Assert.Equal("https://example.com/hls.zip", e.AssetUrl);
        Assert.Equal("1.7.0", e.MinAppVersion);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void ParseCatalog_tolerates_malformed_json()
    {
        Assert.Empty(PluginCatalogService.ParseCatalog("not json", new Dictionary<string, string>()));
        Assert.Empty(PluginCatalogService.ParseCatalog("{}", new Dictionary<string, string>())); // object, not array
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void MeetsMinAppVersion_gates_on_the_running_app_version()
    {
        var app = new System.Version(2, 1, 0);
        Assert.True(PluginCatalogService.MeetsMinAppVersion("2.1.0", app));
        Assert.True(PluginCatalogService.MeetsMinAppVersion("1.7.0", app));
        Assert.False(PluginCatalogService.MeetsMinAppVersion("2.2.0", app));
        // empty / garbage minimums are permissive
        Assert.True(PluginCatalogService.MeetsMinAppVersion("", app));
        Assert.True(PluginCatalogService.MeetsMinAppVersion(null, app));
        Assert.True(PluginCatalogService.MeetsMinAppVersion("not-a-version", app));
    }
}
