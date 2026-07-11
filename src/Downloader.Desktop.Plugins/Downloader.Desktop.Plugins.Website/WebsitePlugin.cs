using Microsoft.Extensions.Logging;
using Downloader.Desktop.Plugins;

namespace Downloader.Desktop.Plugins.Website;

/// <summary>
/// "Website offline copy": crawl a web page — or a whole small site (same-host pages up to a depth/page
/// cap, plus every page requisite: stylesheets, scripts, images, fonts, media, from any host) — rewrite
/// all captured references to relative local paths, and package the result as one .zip that browses
/// offline. The native-.NET equivalent of wget --mirror --page-requisites --convert-links + zip.
///
/// Trigger: the resolver is a FALLBACK (never shadows specific plugins) that offers an
/// "Offline copy (.zip)" variant in the Add window for URLs that serve text/html; choosing it rewrites
/// the item URL to the "websitezip:" scheme, which this plugin's transfer provider owns end-to-end.
/// </summary>
public sealed class WebsitePlugin : IDownloaderPlugin
{
    public string Id => "com.bezzad.website-zip";
    public string Name => "Website offline copy";
    public string Version => typeof(WebsitePlugin).Assembly.GetName().Version is { } v
        ? $"{v.Major}.{v.Minor}.{v.Build}"
        : "0.0.0";
    public string Author => "bezzad";
    public string Description =>
        "Save a web page — or a whole site — for offline reading: pages, styles, scripts, images and " +
        "fonts are captured with links rewritten, packaged as a single .zip.";

    public void Initialize(IPluginContext context)
    {
        context.Logger.LogInformation("Website offline copy plugin initialized");
        context.RegisterResolver(new WebsiteResolver());
        context.RegisterTransferProvider(new WebsiteTransferProvider(context.Logger));
    }
}
