using Microsoft.Extensions.Logging;

namespace Downloader.Desktop.Plugins.Ollama;

/// <summary>
/// Bundled plugin: paste an Ollama model name (<c>gemma3:12b</c>), an <c>ollama.com/library/…</c> link,
/// or a <c>huggingface.co/&lt;owner&gt;/&lt;repo&gt;</c> model repository → the resolver turns it into a
/// direct download URL (the app's engine downloads it multipart like any file); after completion the
/// "Add to Ollama" post-download action verifies the checksum and installs it into the local Ollama store.
/// <para>
/// HuggingFace lives here rather than in a plugin of its own because the destination is identical: a GGUF
/// is only useful once it is in the local store, and that machinery is all here. The two resolvers claim
/// disjoint inputs (a bare model reference or ollama.com vs. a huggingface.co repository), as do the two
/// post-download actions, so neither can shadow the other.
/// </para>
/// </summary>
public sealed class OllamaPlugin : IDownloaderPlugin
{
    public string Id => "com.bezzad.ollama-models";
    public string Name => "Ollama Models";
    public string Version => "1.2.0";
    public string Author => "bezzad";
    public string Description =>
        "Download Ollama models by name (e.g. gemma3:12b), by ollama.com link, or from a HuggingFace model "
        + "repository (choose which GGUF quantisation), then add them to Ollama in one click.";

    public void Initialize(IPluginContext context)
    {
        context.Logger.LogInformation("Ollama Models plugin initialized");
        var registry = new HttpOllamaRegistry();
        context.RegisterResolver(new OllamaResolver(registry));
        context.RegisterPostDownloadAction(new AddToOllamaAction(registry));

        var huggingFace = new HttpHuggingFaceApi();
        context.RegisterResolver(new HuggingFaceResolver(huggingFace));
        context.RegisterPostDownloadAction(new AddHuggingFaceToOllamaAction(huggingFace));
    }
}

/// <summary>Resolves a model reference to its GGUF blob URL via the registry manifest.</summary>
public sealed class OllamaResolver : ILinkResolver
{
    private readonly IOllamaRegistry _registry;
    public OllamaResolver(IOllamaRegistry registry) => _registry = registry;

    public bool CanResolve(string url) => OllamaModelRef.TryParse(url, out _);

    public Task<DownloadPlan> ResolveAsync(string url, CancellationToken cancellationToken)
        => ResolveAsync(url, options: null, cancellationToken);

    /// <summary>The model's available tags as variants when the user pasted a TAG-LESS reference (paste
    /// "gemma3" → pick gemma3:4b / gemma3:12b / …). A fully-tagged paste offers no choices.</summary>
    public async Task<IReadOnlyList<LinkVariant>?> GetVariantsAsync(
        string url, ResolveOptions? options, CancellationToken cancellationToken)
    {
        if (!OllamaModelRef.TryParse(url, out var model) || model!.HasExplicitTag)
            return null;

        var tags = await _registry.GetTagsAsync(model, cancellationToken).ConfigureAwait(false);
        if (tags.Count <= 1)
            return null;
        // Each tag IS its own resolvable reference, so hand it back as a substitute input — the created
        // download then parses/installs as that exact tag (post-download action included).
        var prefix = model.Namespace == "library" ? model.Model : model.PathNamespaceModel;
        return tags.Select(t => new LinkVariant
        {
            Id = t,
            Label = $"{prefix}:{t}",
            IsDefault = t == "latest",
            SubstituteUrl = $"{prefix}:{t}",
        }).ToList();
    }

    public async Task<DownloadPlan> ResolveAsync(string url, ResolveOptions? options, CancellationToken cancellationToken)
    {
        if (!OllamaModelRef.TryParse(url, out var model))
            throw new InvalidOperationException($"'{url}' is not an Ollama model reference.");

        if (!string.IsNullOrEmpty(options?.VariantId))
            model = model!.WithTag(options.VariantId);

        var manifest = await _registry.GetManifestAsync(model!, cancellationToken).ConfigureAwait(false);
        var layer = manifest.ModelLayer!;
        return new DownloadPlan
        {
            SuggestedFileName = $"{model!.Model.Replace('/', '-')}-{model.Tag}.gguf",
            Parts = new[]
            {
                new DownloadPart { Url = _registry.BlobUrl(model, layer.Digest), ExpectedSize = layer.Size }
            }
        };
    }
}

/// <summary>"Add to Ollama": verify + install the downloaded blob into the local store.</summary>
public sealed class AddToOllamaAction : IPostDownloadAction
{
    private readonly IOllamaRegistry _registry;
    /// <summary>Test seam: overrides the store root (null → $OLLAMA_MODELS / ~/.ollama/models).</summary>
    public string? StoreRootOverride { get; set; }

    public AddToOllamaAction(IOllamaRegistry registry) => _registry = registry;

    public string Label => "Add to Ollama";

    public bool CanOffer(string sourceUrl, string filePath) =>
        OllamaModelRef.TryParse(sourceUrl, out _) && !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath);

    public async Task ExecuteAsync(string sourceUrl, string filePath, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (!OllamaModelRef.TryParse(sourceUrl, out var model))
            throw new InvalidOperationException($"'{sourceUrl}' is not an Ollama model reference.");
        await new OllamaInstaller(_registry)
            .InstallAsync(model!, filePath, StoreRootOverride, progress, cancellationToken)
            .ConfigureAwait(false);
    }
}
