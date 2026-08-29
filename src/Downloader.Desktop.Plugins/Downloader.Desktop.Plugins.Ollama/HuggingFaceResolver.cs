namespace Downloader.Desktop.Plugins.Ollama;

/// <summary>
/// Turns a HuggingFace model repository link into a download: the repository's GGUF files become
/// selectable variants (quantisation + size), and the chosen one resolves to its direct download address.
/// A link that already names a file resolves straight to that file.
/// <para>
/// It lives beside the Ollama resolver rather than in a plugin of its own because the destination is the
/// same: a downloaded GGUF is only useful once it is in the local Ollama store, and all of that machinery
/// is already here.
/// </para>
/// </summary>
public sealed class HuggingFaceResolver : ILinkResolver
{
    private readonly IHuggingFaceApi _api;

    public HuggingFaceResolver(IHuggingFaceApi api) => _api = api;

    /// <summary>Pure and network-free by contract — the Add window asks this on every keystroke.</summary>
    public bool CanResolve(string url) => HuggingFaceModelRef.TryParse(url, out _);

    public Task<DownloadPlan> ResolveAsync(string url, CancellationToken cancellationToken)
        => ResolveAsync(url, options: null, cancellationToken);

    public async Task<IReadOnlyList<LinkVariant>?> GetVariantsAsync(
        string url, ResolveOptions? options, CancellationToken cancellationToken)
    {
        if (!HuggingFaceModelRef.TryParse(url, out var model) || model!.HasFile)
            return null; // a link to one file offers no choice

        var models = ModelFiles(await _api.ListFilesAsync(model, cancellationToken).ConfigureAwait(false), model);
        if (models.Count <= 1)
            return null; // one file (or none) — nothing to ask about; resolve handles both

        return models
            .OrderBy(f => f.Size)
            .Select((f, i) => new LinkVariant
            {
                Id = f.Path,
                Label = $"{f.Quantisation} ({FormatSize(f.Size)})",
                Description = f.Name,
                ExpectedSize = f.Size > 0 ? f.Size : null,
                // Default to the smallest that is still a full model: it downloads fastest and runs on the
                // most machines, and someone who wants a bigger quantisation is choosing deliberately.
                IsDefault = i == 0,
            })
            .ToList();
    }

    public async Task<DownloadPlan> ResolveAsync(string url, ResolveOptions? options, CancellationToken cancellationToken)
    {
        if (!HuggingFaceModelRef.TryParse(url, out var model))
            throw new InvalidOperationException($"'{url}' is not a HuggingFace model repository.");

        var chosen = model!;
        long? size = null;

        if (!chosen.HasFile)
        {
            var files = await _api.ListFilesAsync(chosen, cancellationToken).ConfigureAwait(false);
            var models = ModelFiles(files, chosen);

            var pick = !string.IsNullOrEmpty(options?.VariantId)
                ? models.FirstOrDefault(f => f.Path == options!.VariantId)
                : models.OrderBy(f => f.Size).FirstOrDefault();

            if (pick is null)
                throw new InvalidOperationException(NoModelFileMessage(chosen, files));

            chosen = chosen.WithFile(pick.Path);
            size = pick.Size > 0 ? pick.Size : null;
        }

        return new DownloadPlan
        {
            SuggestedFileName = chosen.FilePath!.Contains('/')
                ? chosen.FilePath[(chosen.FilePath.LastIndexOf('/') + 1)..]
                : chosen.FilePath,
            Parts = new[]
            {
                new DownloadPart { Url = chosen.DownloadUrl, Kind = PartKind.Combined, ExpectedSize = size },
            },
            PostProcess = PostProcess.None,
        };
    }

    /// <summary>The repository's downloadable model files: GGUF, and whole ones. A sharded set is
    /// deliberately excluded here and named in the failure message instead of being offered as a pile of
    /// pieces that Ollama cannot load.</summary>
    internal static IReadOnlyList<HuggingFaceFile> ModelFiles(
        IReadOnlyList<HuggingFaceFile> files, HuggingFaceModelRef model) =>
        files.Where(f => f.IsGguf && !f.IsShard).ToList();

    /// <summary>Why a repository yielded nothing to download — distinguishing "there are no model files
    /// here at all" from "the only model here is split into shards", because the second is a limitation of
    /// this plugin and the user deserves to be told that rather than left guessing.</summary>
    internal static string NoModelFileMessage(HuggingFaceModelRef model, IReadOnlyList<HuggingFaceFile> files)
    {
        if (files.Any(f => f.IsGguf && f.IsShard))
            return $"'{model.RepoId}' only publishes its model as a split (sharded) GGUF set, which this "
                   + "plugin can't join back together yet. Look for a single-file GGUF of the same model.";

        return $"'{model.RepoId}' has no GGUF model file to download. It may publish its weights in "
               + "another format (Safetensors), which Ollama can't run directly.";
    }

    private static string FormatSize(long bytes) =>
        bytes <= 0 ? "size unknown"
        : bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.#} GB"
        : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0} MB"
        : $"{bytes / (double)(1L << 10):0} KB";
}
