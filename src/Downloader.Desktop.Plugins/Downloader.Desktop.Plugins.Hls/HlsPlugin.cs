using Downloader.Desktop.Plugins;
using Downloader.Desktop.Plugins.Hls.Dash;
using Microsoft.Extensions.Logging;

namespace Downloader.Desktop.Plugins.Hls;

/// <summary>
/// The streaming-media plugin entry point. Registers an <see cref="HlsResolver"/> (expands an <c>.m3u8</c>
/// master or media playlist into segment parts, with a quality picker on master playlists), a
/// <see cref="DashResolver"/> (the same for an MPEG-DASH <c>.mpd</c> manifest, whose separate video and
/// audio representations become two streams in one plan), and an <see cref="HlsPostProcessor"/> (AES-128
/// decrypt + concat + ffmpeg remux/mux). The host downloads the parts between resolve and post-process.
/// The two resolvers claim disjoint extensions, so neither can shadow the other.
/// </summary>
public sealed class HlsPlugin : IDownloaderPlugin, IHasRuntimeDependencies
{
    public string Id => "com.bezzad.hls";
    public string Name => "Streaming media (HLS & DASH)";
    // Derived from the assembly (set by the csproj <Version>) so the runtime-reported version and the
    // release catalog's version share ONE source — otherwise a bumped catalog vs. a stale hardcoded
    // string here would make the update check prompt forever. Major.Minor.Build mirrors the app's own
    // UpdateService.CurrentVersion.
    public string Version => typeof(HlsPlugin).Assembly.GetName().Version is { } v
        ? $"{v.Major}.{v.Minor}.{v.Build}"
        : "2.0.0";
    public string Author => "bezzad";
    public string Description =>
        "Downloads adaptive streams — HLS (.m3u8) and MPEG-DASH (.mpd): lists the available qualities, " +
        "downloads the chosen (or best) one as segments, and assembles them into a playable file " +
        "(AES-128 decrypt, concat, and ffmpeg remux or video+audio mux).";

    private FfmpegBinary? _ffmpeg;

    public void Initialize(IPluginContext context)
    {
        var http = new HttpClient();
        _ffmpeg = new FfmpegBinary(context.DataDirectory, http, context.Logger);

        context.RegisterResolver(new HlsResolver(http, logger: context.Logger));
        context.RegisterResolver(new DashResolver(http, logger: context.Logger));
        context.RegisterPostProcessor(new HlsPostProcessor(_ffmpeg, http, logger: context.Logger));

        context.Logger.LogInformation("Streaming-media plugin initialized: HLS + DASH (data dir: {Dir})",
            context.DataDirectory);
    }

    /// <summary>ffmpeg — not bundled in the plugin package, fetched by the host (resumable, with
    /// progress) at Add-time via <see cref="FfmpegBinary.GetDependency"/>.</summary>
    public IReadOnlyList<PluginBinaryDependency> GetRequiredDependencies(string dataDirectory)
    {
        var ffmpeg = _ffmpeg ?? new FfmpegBinary(dataDirectory);
        return new[] { ffmpeg.GetDependency() };
    }
}
