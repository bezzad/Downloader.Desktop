using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Downloader.Desktop.Plugins.SiteMedia;

/// <summary>
/// Assembles what the host downloaded for an extracted page: the video-only and audio-only files the site
/// serves separately become one playable file (<see cref="PostProcessKind.Mux"/>). A page that resolved to
/// a single progressive file never reaches here — its plan carries no post-process at all.
/// </summary>
public sealed class MuxPostProcessor : IPostProcessor
{
    private readonly IMediaMuxer _muxer;
    private readonly ILogger _log;

    public MuxPostProcessor(IMediaMuxer muxer, ILogger? logger = null)
    {
        _muxer = muxer ?? throw new ArgumentNullException(nameof(muxer));
        _log = logger ?? NullLogger.Instance;
    }

    public bool CanProcess(PostProcess plan) => plan.Kind is PostProcessKind.Mux;

    public async Task<string> ProcessAsync(
        IReadOnlyList<string> inputFiles,
        PostProcess plan,
        string outputPath,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        if (plan.Kind != PostProcessKind.Mux)
            throw new NotSupportedException($"This post-processor only handles Mux, not {plan.Kind}.");
        if (inputFiles.Count != 2)
            throw new InvalidOperationException(
                $"Muxing needs exactly one video and one audio file, but {inputFiles.Count} were downloaded.");

        // Plan order is the resolver's: video part first, audio second (see SiteMediaResolver).
        _log.LogInformation("Muxing video+audio into {Output}", outputPath);
        progress.Report(0);
        await _muxer.MuxAsync(inputFiles[0], inputFiles[1], outputPath, cancellationToken).ConfigureAwait(false);
        progress.Report(1);
        return outputPath;
    }
}
