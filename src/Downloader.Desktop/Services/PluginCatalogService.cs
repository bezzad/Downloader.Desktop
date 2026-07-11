using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;

namespace Downloader.Desktop.Services;

/// <summary>
/// Discovers OPTIONAL (catalog-tier) plugins by reading <c>plugins-catalog.json</c> off the latest GitHub
/// Release — the same release the app's own <see cref="UpdateService"/> checks. Optional plugins are not
/// bundled with the app; the catalog is how the user finds, installs, and later updates them. Every method
/// is failure-tolerant (offline / rate-limited / no release / malformed json) → returns an empty list or
/// false, never throws to callers, so the Plugins page degrades gracefully.
/// </summary>
public static class PluginCatalogService
{
    private const string Owner = "bezzad";
    private const string Repo = "Downloader.Desktop";
    private const string CatalogAssetName = "plugins-catalog.json";

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
    /// Fetches the catalog from the latest release, resolving each entry's download URL from that same
    /// release's asset list. Returns an empty list on any failure.
    /// </summary>
    public static async Task<IReadOnlyList<CatalogPluginInfo>> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return Array.Empty<CatalogPluginInfo>();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;

            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return Array.Empty<CatalogPluginInfo>();

            // Map assetName -> download URL for this release, and locate the catalog manifest asset.
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
                return Array.Empty<CatalogPluginInfo>();

            var json = await Http.GetStringAsync(catalogUrl, ct).ConfigureAwait(false);
            return ParseCatalog(json, urls);
        }
        catch
        {
            return Array.Empty<CatalogPluginInfo>();
        }
    }

    /// <summary>Parses the catalog JSON, attaching each entry's resolved <c>AssetUrl</c>. Entries whose
    /// asset isn't present on the release (or lack an id/asset/sha256) are skipped. Pure + testable.</summary>
    public static IReadOnlyList<CatalogPluginInfo> ParseCatalog(string json, IReadOnlyDictionary<string, string> assetUrls)
    {
        var list = new List<CatalogPluginInfo>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return list;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                string Str(string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var id = Str("id");
                var assetName = Str("assetName");
                var sha256 = Str("sha256");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(assetName) || string.IsNullOrWhiteSpace(sha256))
                    continue;
                assetUrls.TryGetValue(assetName, out var assetUrl);
                if (string.IsNullOrWhiteSpace(assetUrl))
                    continue; // asset named in the catalog isn't attached to the release — skip
                list.Add(new CatalogPluginInfo
                {
                    Id = id,
                    Name = Str("name") ?? id,
                    Description = Str("description") ?? "",
                    Version = Str("version") ?? "",
                    AssetName = assetName,
                    AssetUrl = assetUrl,
                    Sha256 = sha256,
                    MinAppVersion = Str("minAppVersion") ?? "",
                });
            }
        }
        catch
        {
            // malformed json → whatever parsed so far (usually empty)
        }
        return list;
    }

    /// <summary>Downloads a catalog plugin's asset to <paramref name="destPath"/>. Returns false on error.</summary>
    public static async Task<bool> DownloadAssetAsync(CatalogPluginInfo info, string destPath, CancellationToken ct = default)
    {
        try
        {
            if (info == null || string.IsNullOrWhiteSpace(info.AssetUrl))
                return false;
            using var resp = await Http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return false;
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = File.Create(destPath);
            await src.CopyToAsync(dst, ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True if the catalog version is strictly newer than the installed one (reuses the app's
    /// version parsing so tag/version quirks compare identically to the self-update check).</summary>
    public static bool IsNewer(string catalogVersion, string installedVersion)
    {
        var remote = UpdateService.Normalize(catalogVersion);
        var local = UpdateService.Normalize(installedVersion);
        return remote != null && local != null && remote > local;
    }

    /// <summary>True when this app is new enough for a catalog entry's <c>minAppVersion</c> (a plugin can
    /// require host plumbing a newer app introduced — e.g. the website plugin needs the transfer path).
    /// An empty/unparsable minimum is permissive. Pure for tests via <paramref name="appVersion"/>.</summary>
    public static bool MeetsMinAppVersion(string minAppVersion, Version appVersion = null)
    {
        var min = UpdateService.Normalize(minAppVersion);
        return min == null || (appVersion ?? UpdateService.CurrentVersion) >= min;
    }

    /// <summary>
    /// Install (or update) an optional plugin from a catalog entry: if a copy is already loaded it is
    /// unloaded first (update swap), then the asset is downloaded to a temp file and handed to
    /// <see cref="PluginManager.InstallFromZipAsync(string,string,string,CancellationToken)"/>, which
    /// verifies the SHA-256 before extracting/loading. Once loaded, any external runtime binaries the
    /// plugin declares (<see cref="PluginManager.GetRuntimeDependencies"/>, e.g. the HLS plugin's
    /// ffmpeg/yt-dlp) are fetched (resumable, with progress) before this call reports success — a
    /// dependency failure or cancellation rolls the plugin back out (<see cref="PluginManager.RemovePlugin"/>)
    /// so it never appears "installed" without what it needs to actually run. The temp plugin-package
    /// download is always cleaned up; a partial dependency download is left in place to resume next time.
    /// This is the single path both the Plugins page's Add button and the update-accept flow use.
    /// </summary>
    public static async Task<PluginInstallResult> InstallOrUpdateAsync(PluginManager manager,
        CatalogPluginInfo info, CancellationToken ct = default,
        IProgress<PluginDependencyProgress> dependencyProgress = null)
    {
        if (manager == null || info == null)
            return PluginInstallResult.Fail("Nothing to install.");

        var tmp = Path.Combine(Path.GetTempPath(), $"plugin-dl-{Guid.NewGuid():N}.zip");
        PluginInstallResult result;
        try
        {
            if (!await DownloadAssetAsync(info, tmp, ct).ConfigureAwait(false))
                return PluginInstallResult.Fail("Could not download the plugin. Please check your connection and try again.");
            // Update swap: drop the currently-loaded copy so the loader will accept the new one
            // (registration is idempotent by id, so a still-loaded old copy would block the reload).
            // Done only AFTER the download succeeded — removing first meant a failed download left the
            // plugin silently uninstalled behind a stale "installed" row.
            if (manager.IsInstalled(info.Id))
                manager.RemovePlugin(info.Id);
            result = await manager.InstallFromZipAsync(tmp, info.Sha256, info.Id, ct).ConfigureAwait(false);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
        }

        if (!result.Success)
            return result;

        var deps = manager.GetRuntimeDependencies(info.Id);
        if (deps.Count == 0)
            return result;

        try
        {
            await PluginDependencyInstaller.EnsureAllAsync(deps, dependencyProgress, ct).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException)
        {
            manager.RemovePlugin(info.Id);
            throw;
        }
        catch (Exception ex)
        {
            manager.RemovePlugin(info.Id);
            return PluginInstallResult.Fail($"Installed, but a required component could not be downloaded: {ex.Message}");
        }
    }
}
