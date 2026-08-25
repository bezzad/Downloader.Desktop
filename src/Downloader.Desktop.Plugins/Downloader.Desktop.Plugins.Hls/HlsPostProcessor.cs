using System.Globalization;
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
    /// <summary>Fetches an AES-128 key, given the key URI and the download's request headers (which may be
    /// null). The headers are a parameter rather than baked into an <see cref="HttpClient"/> because one
    /// processor instance serves every download.</summary>
    private readonly Func<string, IReadOnlyDictionary<string, string>?, CancellationToken, Task<byte[]>> _keyFetcher;
    private readonly ILogger _log;

    public HlsPostProcessor(
        IFfmpeg ffmpeg,
        HttpClient? http = null,
        Func<string, IReadOnlyDictionary<string, string>?, CancellationToken, Task<byte[]>>? keyFetcher = null,
        ILogger? logger = null)
    {
        _ffmpeg = ffmpeg ?? throw new ArgumentNullException(nameof(ffmpeg));
        _log = logger ?? NullLogger.Instance;
        if (keyFetcher is not null)
            _keyFetcher = keyFetcher;
        else
        {
            var client = http ?? new HttpClient();
            _keyFetcher = async (uri, headers, ct) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                if (headers is { Count: > 0 })
                    foreach (var (name, value) in headers)
                        // TryAdd, not Add: a value the server sent us may not satisfy .NET's validation for
                        // that header, and a rejected header must not cost us the key request entirely.
                        request.Headers.TryAddWithoutValidation(name, value);
                using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            };
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

        var groups = recipe.StreamsOrSingle();
        if (groups.Count is 0 or > 2)
            throw new NotSupportedException(
                $"A concat recipe must describe one or two streams (video, audio) but describes {groups.Count}.");

        int expected = groups.Sum(g => g.FileCount);
        if (inputFiles.Count != expected)
            throw new InvalidOperationException(
                $"Expected {expected} input files across {groups.Count} stream(s) but got {inputFiles.Count}.");
        if (recipe.Segments.Count != groups.Sum(g => g.SegmentCount))
            throw new InvalidOperationException(
                $"Recipe lists {recipe.Segments.Count} segments but its streams account for {groups.Sum(g => g.SegmentCount)}.");

        var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        Directory.CreateDirectory(outputDir);

        var keyCache = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var temporaries = new List<string>();
        var streamFiles = new List<string>(groups.Count);
        int fileIdx = 0, segIdx = 0, done = 0;

        try
        {
            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];

                // A whole-file stream (a DASH SegmentBase representation) needs no concatenation — copying a
                // multi-gigabyte file just to hand it to ffmpeg would be pure waste, so it is used in place.
                if (group.FileCount == 1 && !group.HasInitSegment && !recipe.Segments[segIdx].Encrypted)
                {
                    streamFiles.Add(inputFiles[fileIdx]);
                    fileIdx++;
                    segIdx++;
                    progress.Report(0.85 * ++done / expected);
                    continue;
                }

                var concatPath = Path.Combine(
                    outputDir,
                    Path.GetFileNameWithoutExtension(outputPath)
                    + (groups.Count > 1 ? $".s{g.ToString(CultureInfo.InvariantCulture)}" : string.Empty)
                    + ".concat" + recipe.IntermediateExtension);
                temporaries.Add(concatPath);
                streamFiles.Add(concatPath);

                await using var output = File.Create(concatPath);

                if (group.HasInitSegment)
                {
                    await AppendAsync(output, inputFiles[fileIdx], cancellationToken).ConfigureAwait(false);
                    fileIdx++;
                    progress.Report(0.85 * ++done / expected);
                }

                for (int s = 0; s < group.SegmentCount; s++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = recipe.Segments[segIdx];
                    var bytes = await File.ReadAllBytesAsync(inputFiles[fileIdx], cancellationToken).ConfigureAwait(false);

                    if (entry.Encrypted)
                    {
                        var key = await GetKeyAsync(entry.KeyUri!, recipe.KeyHeaders, keyCache, cancellationToken)
                            .ConfigureAwait(false);
                        var iv = Convert.FromHexString(entry.IvHex
                            ?? throw new InvalidOperationException("Encrypted segment has no IV."));
                        bytes = Aes128.DecryptCbc(bytes, key, iv);
                    }

                    await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    fileIdx++;
                    segIdx++;
                    // reserve the final 15% for the ffmpeg step
                    progress.Report(0.85 * ++done / expected);
                }
            }

            _log.LogInformation("Concat complete ({Streams} stream(s), {Segments} segments) → {Output}",
                groups.Count, recipe.Segments.Count, outputPath);

            if (streamFiles.Count == 1)
                await _ffmpeg.RemuxAsync(streamFiles[0], outputPath, cancellationToken).ConfigureAwait(false);
            else
                await _ffmpeg.MuxAsync(streamFiles[0], streamFiles[1], outputPath, cancellationToken).ConfigureAwait(false);

            progress.Report(1.0);
            return outputPath;
        }
        finally
        {
            foreach (var temp in temporaries)
            {
                try { File.Delete(temp); } catch (IOException) { /* best effort */ }
            }
        }
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

    private async Task<byte[]> GetKeyAsync(string keyUri, IReadOnlyDictionary<string, string>? headers,
        Dictionary<string, byte[]> cache, CancellationToken ct)
    {
        if (cache.TryGetValue(keyUri, out var cached)) return cached;
        var key = await _keyFetcher(keyUri, headers, ct).ConfigureAwait(false);
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
