using System.Text.Json;
using Downloader.Desktop.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Downloader.Desktop.Plugins.Hls;

/// <summary>
/// Assembles the downloaded HLS segment files (in plan order) into one playable file: AES-128-decrypt the
/// encrypted segments per the <see cref="ConcatRecipe"/>, concatenate in order, then remux to MP4 via ffmpeg
/// (<c>-c copy</c>). Implements the SDK <see cref="PostProcessKind.Concat"/> step, plus
/// <see cref="PostProcessKind.Mux"/> (combine an extracted video-only + audio-only pair) for site
/// extractions whose best format is split streams.
/// </summary>
public sealed class HlsPostProcessor : IPostProcessor
{
    private readonly IFfmpeg _ffmpeg;
    private readonly Func<string, CancellationToken, Task<byte[]>> _keyFetcher;
    private readonly ILogger _log;

    public HlsPostProcessor(
        IFfmpeg ffmpeg,
        HttpClient? http = null,
        Func<string, CancellationToken, Task<byte[]>>? keyFetcher = null,
        ILogger? logger = null)
    {
        _ffmpeg = ffmpeg ?? throw new ArgumentNullException(nameof(ffmpeg));
        _log = logger ?? NullLogger.Instance;
        if (keyFetcher is not null)
            _keyFetcher = keyFetcher;
        else
        {
            var client = http ?? new HttpClient();
            _keyFetcher = (uri, ct) => client.GetByteArrayAsync(uri, ct);
        }
    }

    public bool CanProcess(PostProcess plan) =>
        plan.Kind is PostProcessKind.Concat or PostProcessKind.Mux;

    public async Task<string> ProcessAsync(
        IReadOnlyList<string> inputFiles,
        PostProcess plan,
        string outputPath,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        if (plan.Kind == PostProcessKind.Mux)
            return await MuxAsync(inputFiles, outputPath, progress, cancellationToken).ConfigureAwait(false);

        if (plan.Kind != PostProcessKind.Concat)
            throw new NotSupportedException($"HLS post-processor only handles Concat and Mux, not {plan.Kind}.");
        if (string.IsNullOrEmpty(plan.Recipe))
            throw new InvalidOperationException("Concat plan is missing its recipe.");

        var recipe = JsonSerializer.Deserialize<ConcatRecipe>(plan.Recipe)
                     ?? throw new InvalidOperationException("Concat recipe could not be deserialized.");

        int expected = recipe.Segments.Count + (recipe.HasInitSegment ? 1 : 0);
        if (inputFiles.Count != expected)
            throw new InvalidOperationException(
                $"Expected {expected} input files (init: {recipe.HasInitSegment}, segments: {recipe.Segments.Count}) but got {inputFiles.Count}.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        var concatPath = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(outputPath))!,
            Path.GetFileNameWithoutExtension(outputPath) + ".concat.ts");

        var keyCache = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        int idx = 0;

        await using (var output = File.Create(concatPath))
        {
            if (recipe.HasInitSegment)
            {
                await AppendAsync(output, inputFiles[idx], cancellationToken).ConfigureAwait(false);
                idx++;
                progress.Report(0.05);
            }

            for (int s = 0; s < recipe.Segments.Count; s++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = recipe.Segments[s];
                var bytes = await File.ReadAllBytesAsync(inputFiles[idx], cancellationToken).ConfigureAwait(false);

                if (entry.Encrypted)
                {
                    var key = await GetKeyAsync(entry.KeyUri!, keyCache, cancellationToken).ConfigureAwait(false);
                    var iv = Convert.FromHexString(entry.IvHex
                        ?? throw new InvalidOperationException("Encrypted segment has no IV."));
                    bytes = Aes128.DecryptCbc(bytes, key, iv);
                }

                await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                idx++;
                // reserve the final 15% for the ffmpeg remux step
                progress.Report(0.05 + 0.80 * (s + 1) / recipe.Segments.Count);
            }
        }

        _log.LogInformation("HLS concat complete ({Segments} segments) → remuxing to {Output}",
            recipe.Segments.Count, outputPath);

        await _ffmpeg.RemuxAsync(concatPath, outputPath, cancellationToken).ConfigureAwait(false);
        progress.Report(1.0);

        try { File.Delete(concatPath); } catch (IOException) { /* best effort */ }

        return outputPath;
    }

    /// <summary>Mux an extracted video-only + audio-only pair (plan parts in [video, audio] order) into one file.</summary>
    private async Task<string> MuxAsync(
        IReadOnlyList<string> inputFiles, string outputPath, IProgress<double> progress, CancellationToken ct)
    {
        if (inputFiles.Count != 2)
            throw new InvalidOperationException($"Mux expects exactly 2 input files (video, audio) but got {inputFiles.Count}.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        progress.Report(0.1);
        _log.LogInformation("Muxing video+audio → {Output}", outputPath);
        await _ffmpeg.MuxAsync(inputFiles[0], inputFiles[1], outputPath, ct).ConfigureAwait(false);
        progress.Report(1.0);
        return outputPath;
    }

    private async Task<byte[]> GetKeyAsync(string keyUri, Dictionary<string, byte[]> cache, CancellationToken ct)
    {
        if (cache.TryGetValue(keyUri, out var cached)) return cached;
        var key = await _keyFetcher(keyUri, ct).ConfigureAwait(false);
        if (key.Length != 16)
            throw new InvalidOperationException($"AES-128 key from {keyUri} was {key.Length} bytes, expected 16.");
        cache[keyUri] = key;
        return key;
    }

    private static async Task AppendAsync(Stream output, string file, CancellationToken ct)
    {
        await using var input = File.OpenRead(file);
        await input.CopyToAsync(output, ct).ConfigureAwait(false);
    }
}
