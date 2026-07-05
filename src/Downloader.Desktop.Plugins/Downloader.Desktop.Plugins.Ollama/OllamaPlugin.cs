using Microsoft.Extensions.Logging;

namespace Downloader.Desktop.Plugins.Ollama;

/// <summary>
/// Bundled plugin: paste an Ollama model name (<c>gemma3:12b</c>) or an
/// <c>ollama.com/library/…</c> link → the resolver turns it into the model blob's direct download URL
/// (the app's engine downloads it multipart like any file); after completion the "Add to Ollama"
/// post-download action verifies the checksum and installs it into the local Ollama store.
/// </summary>
public sealed class OllamaPlugin : IDownloaderPlugin
{
    public string Id => "com.bezzad.ollama-models";
    public string Name => "Ollama Models";
    public string Version => "1.0.0";
    public string Author => "bezzad";
    public string Description =>
        "Download Ollama models by name (e.g. gemma3:12b) or ollama.com link, then add them to Ollama in one click.";

    public void Initialize(IPluginContext context)
    {
        context.Logger.LogInformation("Ollama Models plugin initialized");
        var registry = new HttpOllamaRegistry();
        context.RegisterResolver(new OllamaResolver(registry));
        context.RegisterPostDownloadAction(new AddToOllamaAction(registry));
    }
}

/// <summary>Resolves a model reference to its GGUF blob URL via the registry manifest.</summary>
public sealed class OllamaResolver : ILinkResolver
{
    private readonly IOllamaRegistry _registry;
    public OllamaResolver(IOllamaRegistry registry) => _registry = registry;

    public bool CanResolve(string url) => OllamaModelRef.TryParse(url, out _);

    public async Task<DownloadPlan> ResolveAsync(string url, CancellationToken cancellationToken)
    {
        if (!OllamaModelRef.TryParse(url, out var model))
            throw new InvalidOperationException($"'{url}' is not an Ollama model reference.");

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
