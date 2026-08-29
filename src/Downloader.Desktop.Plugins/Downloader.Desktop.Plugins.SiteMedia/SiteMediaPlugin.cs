using Microsoft.Extensions.Logging;

namespace Downloader.Desktop.Plugins.SiteMedia;

/// <summary>
/// The site-media plugin entry point. Registers a <see cref="SiteMediaResolver"/> (a supported site's page
/// URL → the real media stream(s), with a quality picker) and a <see cref="MuxPostProcessor"/> that
/// combines a downloaded video-only + audio-only pair into one playable file.
/// <para>
/// This plugin is optional and installed deliberately: it is the only component that runs a third-party
/// extraction tool, which it fetches and sha256-verifies on first use and starts from an absolute path
/// with no shell. It reads no browser profile or cookie store — a signed-in session reaches it only as a
/// cookie file our own browser extension captured for the link the user sent.
/// </para>
/// </summary>
public sealed class SiteMediaPlugin : IDownloaderPlugin, IHasRuntimeDependencies
{
    public string Id => "com.bezzad.site-media";
    public string Name => "Video sites (YouTube and others)";
    // Derived from the assembly (set by the csproj <Version>) so the runtime-reported version and the
    // release catalog's version share ONE source — a stale hardcoded string here would make the update
    // check prompt forever.
    public string Version => typeof(SiteMediaPlugin).Assembly.GetName().Version is { } v
        ? $"{v.Major}.{v.Minor}.{v.Build}"
        : "1.0.0";
    public string Author => "bezzad";
    public string Description =>
        "Downloads the video behind a page on YouTube, X, Instagram, TikTok, Vimeo and other video sites: " +
        "lists the available qualities, downloads the chosen one, and assembles it into a playable file. " +
        "Uses the browser session your Downloader extension sends with the link — never the browser's own " +
        "stored cookies.";

    private FfmpegMuxer? _ffmpeg;
    private YtDlpBinary? _ytDlp;

    public void Initialize(IPluginContext context)
    {
        _ffmpeg = new FfmpegMuxer(context.DataDirectory, logger: context.Logger);
        _ytDlp = new YtDlpBinary(context.DataDirectory, logger: context.Logger);

        context.RegisterResolver(new SiteMediaResolver(_ytDlp, context.Logger));
        context.RegisterPostProcessor(new MuxPostProcessor(_ffmpeg, context.Logger));

        context.Logger.LogInformation("Site-media plugin initialized (data dir: {Dir})", context.DataDirectory);
    }

    /// <summary>The tools this plugin needs, fetched by the host (resumable, with progress) at install
    /// time rather than bundled: ffmpeg to assemble, yt-dlp to extract, and Deno for the JS challenge some
    /// sites require. Each is verified before it is used.</summary>
    public IReadOnlyList<PluginBinaryDependency> GetRequiredDependencies(string dataDirectory)
    {
        var ffmpeg = _ffmpeg ?? new FfmpegMuxer(dataDirectory);
        var ytDlp = _ytDlp ?? new YtDlpBinary(dataDirectory);
        return new[] { ffmpeg.GetDependency() }.Concat(ytDlp.GetDependencies()).ToList();
    }
}
