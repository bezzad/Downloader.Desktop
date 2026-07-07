namespace Downloader.Desktop.Models;

/// <summary>
/// One entry from the release-hosted <c>plugins-catalog.json</c>: an OPTIONAL plugin the app can install
/// or update on demand. Static fields (id/name/description/minAppVersion) come from the manifest; the
/// per-release fields (version/assetName/sha256) are produced by scripts/build-plugins.sh at release time.
/// <see cref="AssetUrl"/> is resolved by <see cref="Services.PluginCatalogService"/> from the same
/// GitHub Release's asset list (the manifest carries only the file name). <see cref="Sha256"/> is verified
/// before the downloaded plugin is ever extracted/loaded.
/// </summary>
public sealed class CatalogPluginInfo
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string Description { get; init; }
    public string Version { get; init; }
    public string AssetName { get; init; }
    public string AssetUrl { get; init; }
    public string Sha256 { get; init; }
    public string MinAppVersion { get; init; }
}
