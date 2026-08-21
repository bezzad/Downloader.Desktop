using Downloader.Desktop.Plugins;
using Microsoft.Extensions.Logging;

namespace Downloader.Desktop.Plugins.Hls;

/// <summary>
/// The HLS plugin entry point. Registers an <see cref="HlsResolver"/> (expands an <c>.m3u8</c> master or
/// media playlist into segment parts, with a quality picker on master playlists) and an
/// <see cref="HlsPostProcessor"/> (AES-128 decrypt + concat + ffmpeg remux). The host downloads the
/// parts between resolve and post-process.
/// </summary>
public sealed class HlsPlugin : IDownloaderPlugin, IHasRuntimeDependencies
{
    public string Id => "com.bezzad.hls";
    public string Name => "HLS (m3u8) downloader";
    // Derived from the assembly (set by the csproj <Version>) so the runtime-reported version and the
    // release catalog's version share ONE source — otherwise a bumped catalog vs. a stale hardcoded
    // string here would make the update check prompt forever. Major.Minor.Build mirrors the app's own
    // UpdateService.CurrentVersion.
    public string Version => typeof(HlsPlugin).Assembly.GetName().Version is { } v
        ? $"{v.Major}.{v.Minor}.{v.Build}"
        : "2.0.0";
    public string Author => "bezzad";
    public string Description =>
        "Downloads HLS (.m3u8) streams: lists the qualities in a master playlist, downloads the chosen " +
        "(or best) rendition as segments, and assembles them (AES-128 decrypt + concat + ffmpeg remux) " +
        "into a playable file.";

    private FfmpegBinary? _ffmpeg;

    public void Initialize(IPluginContext context)
    {
        var http = new HttpClient();
        _ffmpeg = new FfmpegBinary(context.DataDirectory, http, context.Logger);

        context.RegisterResolver(new HlsResolver(http, logger: context.Logger));
        context.RegisterPostProcessor(new HlsPostProcessor(_ffmpeg, http, logger: context.Logger));

        context.Logger.LogInformation("HLS plugin initialized (data dir: {Dir})", context.DataDirectory);
    }

    /// <summary>ffmpeg — not bundled in the plugin package, fetched by the host (resumable, with
    /// progress) at Add-time via <see cref="FfmpegBinary.GetDependency"/>.</summary>
    public IReadOnlyList<PluginBinaryDependency> GetRequiredDependencies(string dataDirectory)
    {
        var ffmpeg = _ffmpeg ?? new FfmpegBinary(dataDirectory);
        return new[] { ffmpeg.GetDependency() };
    }
}
