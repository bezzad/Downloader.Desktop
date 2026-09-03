using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Models;

namespace Downloader.Desktop.Services;

/// <summary>
/// Reads <c>extension-catalog.json</c> off the latest GitHub Release — the same release
/// <see cref="UpdateService"/> and <see cref="PluginCatalogService"/> already look at. This is how the app
/// learns which browser-extension builds exist, what they hash to, and whether a store listing is
/// published for them.
///
/// <para>Fetching from the release rather than bundling the extension in the app is deliberate: it lets the
/// extension be updated without shipping an app release. The trade-off is that the extension can outrun
/// the app's local API, which is what <see cref="ExtensionCatalogEntry.MinAppVersion"/> exists for.</para>
///
/// <para>Every method is failure-tolerant (offline, rate-limited, no release, malformed json) — it returns
/// an empty list, never throws to callers, so the install dialog degrades to "couldn't reach the release"
/// instead of breaking.</para>
/// </summary>
public static class ExtensionCatalogService
{
    private const string Owner = "bezzad";
    private const string Repo = "Downloader.Desktop";
    internal const string CatalogAssetName = "extension-catalog.json";

    /// <summary>Test seam — the release endpoint the catalog is read from. Everything after the fetch
    /// (which assets exist, which entries are usable, which are gated out) is ordinary logic that decides
    /// what the user is offered, and none of it is reachable while the URL is hard-coded at GitHub. Tests
    /// point it at a loopback server; the app never sets it.</summary>
    internal static string ReleasesUrlOverride { get; set; }

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Downloader.Desktop", ver));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    /// <summary>
    /// The extension builds this app can offer, from the latest release. Entries whose asset is not on
    /// that release, or that require a newer app, are dropped. Empty on any failure.
    /// </summary>
    public static async Task<IReadOnlyList<ExtensionCatalogEntry>> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            var url = ReleasesUrlOverride ?? $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return Array.Empty<ExtensionCatalogEntry>();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            if (!doc.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return Array.Empty<ExtensionCatalogEntry>();

            var urls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string catalogUrl = null;
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                var dl = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(dl))
                    continue;
                urls[name] = dl;
                if (string.Equals(name, CatalogAssetName, StringComparison.OrdinalIgnoreCase))
                    catalogUrl = dl;
            }
            if (catalogUrl == null)
                return Array.Empty<ExtensionCatalogEntry>();

            var json = await Http.GetStringAsync(catalogUrl, ct).ConfigureAwait(false);
            return ParseCatalog(json, urls);
        }
        catch
        {
            return Array.Empty<ExtensionCatalogEntry>();
        }
    }

    /// <summary>
    /// Parses the catalog, attaching each entry's resolved <see cref="ExtensionCatalogEntry.AssetUrl"/>.
    /// Pure and testable. Entries are dropped when they lack an id/asset/checksum, when the release does
    /// not carry the asset they name, or when this app is older than their
    /// <see cref="ExtensionCatalogEntry.MinAppVersion"/>.
    /// </summary>
    public static IReadOnlyList<ExtensionCatalogEntry> ParseCatalog(string json,
        IReadOnlyDictionary<string, string> assetUrls, Version appVersion = null)
    {
        var list = new List<ExtensionCatalogEntry>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var e in doc.RootElement.EnumerateArray())
            {
                string Str(string p) =>
                    e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

                var id = Str("id");
                var assetName = Str("assetName");
                var sha256 = Str("sha256");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(assetName) || string.IsNullOrWhiteSpace(sha256))
                    continue;

                var minAppVersion = Str("minAppVersion") ?? "";
                if (!PluginCatalogService.MeetsMinAppVersion(minAppVersion, appVersion))
                    continue; // this app's API can't serve that build — don't offer it

                assetUrls.TryGetValue(assetName, out var assetUrl);
                if (string.IsNullOrWhiteSpace(assetUrl))
                    continue; // named an asset the release doesn't carry

                list.Add(new ExtensionCatalogEntry
                {
                    Id = id,
                    Family = Str("family") ?? "chromium",
                    Name = Str("name") ?? id,
                    Version = Str("version") ?? "",
                    AssetName = assetName,
                    AssetUrl = assetUrl,
                    Sha256 = sha256,
                    MinAppVersion = minAppVersion,
                    StoreUrl = Str("storeUrl"),
                });
            }
        }
        catch
        {
            // malformed json → whatever parsed so far (usually nothing)
        }
        return list;
    }

    /// <summary>True when the published extension version is strictly newer than the one a browser
    /// reported. Reuses the app's version parsing so this compares identically to the self-update and
    /// plugin-update checks.</summary>
    public static bool IsNewer(string catalogVersion, string installedVersion)
        => PluginCatalogService.IsNewer(catalogVersion, installedVersion);

    /// <summary>The newer of two versions, ignoring blanks. Null only when both are blank.</summary>
    public static string Newer(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a)) return string.IsNullOrWhiteSpace(b) ? null : b;
        if (string.IsNullOrWhiteSpace(b)) return a;
        return IsNewer(b, a) ? b : a;
    }

    /// <summary>
    /// The newest build this app could actually install: the published catalog, or the copy bundled
    /// inside the app, whichever is newer.
    ///
    /// <para>Both halves matter. The catalog is empty on every machine whose app predates it — and on any
    /// machine that is simply offline — so a check that only consulted the catalog could never say "your
    /// extension is out of date", which is exactly what it was for. The bundled copy, meanwhile, ships
    /// with the app and is frequently the newer of the two right after an app update.</para>
    /// </summary>
    public static string BestAvailable(IEnumerable<ExtensionCatalogEntry> catalog, string bundledVersion)
        => Newer(NewestVersion(catalog), bundledVersion);

    /// <summary>The newest version any published build carries, or null when the catalog says nothing.</summary>
    public static string NewestVersion(IEnumerable<ExtensionCatalogEntry> catalog)
        => catalog?
            .Select(e => e?.Version)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .OrderByDescending(UpdateService.Normalize)
            .FirstOrDefault();

    /// <summary>
    /// Whether to tell the user their extension is out of date, and with which two versions.
    ///
    /// <para>Pure on purpose — this is the decision the startup check exists to make, and a check like
    /// this is only worth having if it never cries wolf. It says nothing when no extension has reported
    /// (an extension that never called is not out of date, it is not installed), when neither the catalog
    /// nor the app carries a version to compare against, or when everything reported is current.</para>
    ///
    /// <para><paramref name="bundledVersion"/> is what makes this answerable at all today: no published
    /// release carries an extension catalog yet, so with the catalog alone this always said "no" and a
    /// browser running an old copy was never told (reported 2026-09-04, with v1.11.0 loaded in Chrome
    /// against a newer copy inside the app).</para>
    ///
    /// <para><paramref name="reportedVersion"/> is the oldest version reported, so a user with two
    /// browsers is told about the one that actually needs attention.</para>
    /// </summary>
    public static bool ShouldWarnAboutExtension(IEnumerable<string> reportedVersions,
        IEnumerable<ExtensionCatalogEntry> catalog, string bundledVersion,
        out string reportedVersion, out string availableVersion)
    {
        reportedVersion = null;
        var newest = BestAvailable(catalog, bundledVersion);
        availableVersion = newest;
        if (string.IsNullOrWhiteSpace(newest) || reportedVersions == null)
            return false;

        // `newest` is a local because an out parameter cannot be captured by the lambda below.
        var behind = reportedVersions
            .Where(v => !string.IsNullOrWhiteSpace(v) && IsNewer(newest, v))
            .OrderBy(UpdateService.Normalize)
            .FirstOrDefault();
        if (behind == null)
            return false;

        reportedVersion = behind;
        return true;
    }
}
