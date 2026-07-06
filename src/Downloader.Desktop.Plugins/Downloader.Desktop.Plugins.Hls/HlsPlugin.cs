using Downloader.Desktop.Plugins;
using Microsoft.Extensions.Logging;

namespace Downloader.Desktop.Plugins.Hls;

/// <summary>
/// The HLS plugin entry point. Registers an <see cref="HlsResolver"/> (expands an <c>.m3u8</c> link into
/// segment parts) and an <see cref="HlsPostProcessor"/> (AES-128 decrypt + concat + ffmpeg remux). The host
/// downloads the parts between resolve and post-process.
/// </summary>
public sealed class HlsPlugin : IDownloaderPlugin
{
    public string Id => "com.bezzad.hls";
    public string Name => "HLS (m3u8) downloader";
    // Derived from the assembly (set by the csproj <Version>) so the runtime-reported version and the
    // release catalog's version share ONE source — otherwise a bumped catalog vs. a stale hardcoded
    // string here would make the update check prompt forever. Major.Minor.Build mirrors the app's own
    // UpdateService.CurrentVersion.
    public string Version => typeof(HlsPlugin).Assembly.GetName().Version is { } v
        ? $"{v.Major}.{v.Minor}.{v.Build}"
        : "1.1.2";
    public string Author => "bezzad";
    public string Description =>
        "Downloads HLS (.m3u8) streams and videos from supported sites (e.g. x.com): expands the stream " +
        "into segments and assembles them (AES-128 decrypt + concat/mux + ffmpeg remux) into a playable file.";

    public void Initialize(IPluginContext context)
    {
        var http = new HttpClient();
        var ffmpeg = new FfmpegBinary(context.DataDirectory, http, context.Logger);
        var ytDlp = new YtDlpBinary(context.DataDirectory, http, context.Logger);

        context.RegisterResolver(new HlsResolver(http, ytDlp: ytDlp, logger: context.Logger));
        context.RegisterPostProcessor(new HlsPostProcessor(ffmpeg, http, logger: context.Logger));

        context.Logger.LogInformation("HLS plugin initialized (data dir: {Dir})", context.DataDirectory);
    }
}
