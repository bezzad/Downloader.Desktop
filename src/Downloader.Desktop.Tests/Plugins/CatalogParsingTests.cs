using System.Collections.Generic;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins;

/// <summary>
/// Parsing the optional-plugin catalog attached to a release.
///
/// The parser's job is to be strict about what it will offer and permissive about what it will
/// tolerate. Every entry it emits leads to a download-and-load, so an entry missing an id, an asset
/// name or a sha256 must be dropped: without the hash there is nothing to verify the download
/// against, and verification before load is the only thing standing between a tampered asset and
/// arbitrary code running in-process. An entry naming an asset the release does not actually carry
/// is dropped too — offering an Add button that can only 404 is worse than not offering it.
///
/// Conversely, malformed or unexpected JSON must never throw: this runs at startup behind the update
/// check, and an exception there would surface as a broken Settings page rather than an empty list.
/// </summary>
public class CatalogParsingTests
{
    private static readonly IReadOnlyDictionary<string, string> Assets = new Dictionary<string, string>
    {
        ["hls.zip"] = "https://example.invalid/hls.zip",
        ["website.zip"] = "https://example.invalid/website.zip",
    };

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_complete_entry_is_parsed_and_paired_with_its_release_asset()
    {
        const string json = """
        [
          {
            "id": "com.bezzad.hls",
            "name": "Streaming media",
            "description": "HLS and DASH",
            "version": "2.2.1",
            "assetName": "hls.zip",
            "sha256": "deadbeef",
            "minAppVersion": "2.1.0"
          }
        ]
        """;

        var entry = Assert.Single(PluginCatalogService.ParseCatalog(json, Assets));

        Assert.Equal("com.bezzad.hls", entry.Id);
        Assert.Equal("Streaming media", entry.Name);
        Assert.Equal("HLS and DASH", entry.Description);
        Assert.Equal("2.2.1", entry.Version);
        Assert.Equal("hls.zip", entry.AssetName);
        Assert.Equal("deadbeef", entry.Sha256);
        Assert.Equal("2.1.0", entry.MinAppVersion);
        // The URL comes from the release's attached assets, not from the catalog file itself.
        Assert.Equal("https://example.invalid/hls.zip", entry.AssetUrl);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Optional_fields_fall_back_to_sensible_defaults()
    {
        const string json = """
        [ { "id": "com.bezzad.hls", "assetName": "hls.zip", "sha256": "abc" } ]
        """;

        var entry = Assert.Single(PluginCatalogService.ParseCatalog(json, Assets));

        Assert.Equal("com.bezzad.hls", entry.Name); // falls back to the id
        Assert.Equal("", entry.Description);
        Assert.Equal("", entry.Version);
        Assert.Equal("", entry.MinAppVersion);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("""[ { "assetName": "hls.zip", "sha256": "abc" } ]""")]              // no id
    [InlineData("""[ { "id": "x", "sha256": "abc" } ]""")]                            // no asset name
    [InlineData("""[ { "id": "x", "assetName": "hls.zip" } ]""")]                     // no sha256
    [InlineData("""[ { "id": "", "assetName": "hls.zip", "sha256": "abc" } ]""")]     // blank id
    [InlineData("""[ { "id": "x", "assetName": "hls.zip", "sha256": "" } ]""")]       // blank sha256
    public void An_entry_missing_what_verification_needs_is_dropped(string json)
    {
        // No hash means nothing to verify the download against, and verification happens BEFORE the
        // assembly is loaded — so an unverifiable entry must never be offered.
        Assert.Empty(PluginCatalogService.ParseCatalog(json, Assets));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_entry_whose_asset_is_not_attached_to_the_release_is_dropped()
    {
        const string json = """
        [ { "id": "com.bezzad.torrent", "assetName": "torrent.zip", "sha256": "abc" } ]
        """;

        // Offering an Add button that could only ever 404 is worse than not offering it.
        Assert.Empty(PluginCatalogService.ParseCatalog(json, Assets));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Good_entries_survive_a_bad_neighbour()
    {
        const string json = """
        [
          { "id": "com.bezzad.hls", "assetName": "hls.zip", "sha256": "abc" },
          { "id": "broken" },
          { "id": "com.bezzad.website-zip", "assetName": "website.zip", "sha256": "def" }
        ]
        """;

        var entries = PluginCatalogService.ParseCatalog(json, Assets);

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Id == "com.bezzad.hls");
        Assert.Contains(entries, e => e.Id == "com.bezzad.website-zip");
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{ \"not\": \"an array\" }")]
    [InlineData("[")]                                   // truncated
    [InlineData("[ \"a string, not an object\" ]")]
    [InlineData("[ 42 ]")]
    [InlineData("null")]
    public void Malformed_input_yields_an_empty_catalog_rather_than_an_exception(string json)
    {
        // This runs at startup behind the update check; throwing here would surface as a broken
        // Settings page instead of simply "no optional plugins to offer".
        Assert.Empty(PluginCatalogService.ParseCatalog(json, Assets));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_entry_with_wrongly_typed_fields_is_skipped_not_coerced()
    {
        const string json = """
        [ { "id": 42, "assetName": "hls.zip", "sha256": "abc" } ]
        """;

        // A numeric id is not silently turned into "42" — only strings count.
        Assert.Empty(PluginCatalogService.ParseCatalog(json, Assets));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_empty_release_offers_nothing()
    {
        const string json = """[ { "id": "x", "assetName": "hls.zip", "sha256": "abc" } ]""";

        Assert.Empty(PluginCatalogService.ParseCatalog(json, new Dictionary<string, string>()));
        Assert.Empty(PluginCatalogService.ParseCatalog("[]", Assets));
    }
}
