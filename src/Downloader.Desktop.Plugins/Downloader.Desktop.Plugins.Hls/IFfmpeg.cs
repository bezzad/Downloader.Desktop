namespace Downloader.Desktop.Plugins.Hls;

/// <summary>
/// Abstracts the one ffmpeg operation the HLS post-processor needs: remux a concatenated elementary stream
/// (raw .ts / fMP4) into a clean MP4 container with stream copy (no re-encode). Behind an interface so tests
/// stub it and the real provider (which downloads ffmpeg on first use) stays out of the unit tests.
/// </summary>
public interface IFfmpeg
{
    /// <summary>Remux <paramref name="inputFile"/> into <paramref name="outputPath"/> with <c>-c copy</c>.</summary>
    Task RemuxAsync(string inputFile, string outputPath, CancellationToken cancellationToken);

    /// <summary>Mux a separate video-only and audio-only file into one <paramref name="outputPath"/> with
    /// stream copy (no re-encode) — used for extracted DASH/adaptive results that come as split streams.</summary>
    Task MuxAsync(string videoFile, string audioFile, string outputPath, CancellationToken cancellationToken);
}
