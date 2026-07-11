using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Downloader.Desktop.Plugins.Ollama;

/// <summary>One layer of an Ollama (OCI) manifest.</summary>
public sealed record OllamaLayer(string MediaType, string Digest, long Size);

/// <summary>A parsed Ollama registry manifest: the raw JSON (written verbatim into the local store) and
/// its layers (the big <c>…image.model</c> layer is the GGUF weights; the rest are small metadata).</summary>
public sealed class OllamaManifest
{
    public const string ModelMediaType = "application/vnd.ollama.image.model";

    public string RawJson { get; init; } = "";
    public IReadOnlyList<OllamaLayer> Layers { get; init; } = Array.Empty<OllamaLayer>();
    public OllamaLayer? Config { get; init; }

    public OllamaLayer? ModelLayer => Layers.FirstOrDefault(l => l.MediaType == ModelMediaType);

    /// <summary>Everything that must exist in the blob store BESIDES the model layer (config + small layers).</summary>
    public IEnumerable<OllamaLayer> MetadataLayers =>
        (Config is null ? Layers : Layers.Append(Config)).Where(l => l.MediaType != ModelMediaType);

    /// <summary>Parses a registry manifest JSON. Throws with a clear message on unusable content.</summary>
    public static OllamaManifest Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var layers = new List<OllamaLayer>();
        if (root.TryGetProperty("layers", out var layersEl) && layersEl.ValueKind == JsonValueKind.Array)
            foreach (var l in layersEl.EnumerateArray())
                layers.Add(ReadLayer(l));
        OllamaLayer? config = root.TryGetProperty("config", out var cfgEl) ? ReadLayer(cfgEl) : null;
        var manifest = new OllamaManifest { RawJson = json, Layers = layers, Config = config };
        if (manifest.ModelLayer is null)
            throw new InvalidOperationException(
                "The registry manifest has no model layer — this doesn't look like a downloadable Ollama model.");
        return manifest;

        static OllamaLayer ReadLayer(JsonElement el) => new(
            el.TryGetProperty("mediaType", out var mt) ? mt.GetString() ?? "" : "",
            el.TryGetProperty("digest", out var dg) ? dg.GetString() ?? "" : "",
            el.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var v) ? v : 0);
    }
}

/// <summary>Registry access behind an interface so tests can stub it / point it at a loopback server.</summary>
public interface IOllamaRegistry
{
    Task<OllamaManifest> GetManifestAsync(OllamaModelRef model, CancellationToken ct);
    string BlobUrl(OllamaModelRef model, string digest);
    Task DownloadBlobAsync(OllamaModelRef model, string digest, string destinationPath, CancellationToken ct);

    /// <summary>The model's available tags (registry <c>/v2/&lt;name&gt;/tags/list</c>), so the host can
    /// offer them as variants when the user pasted a tag-less reference. Empty on none.</summary>
    Task<IReadOnlyList<string>> GetTagsAsync(OllamaModelRef model, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
}

/// <summary>Talks to the real registry (default <c>https://registry.ollama.ai</c>; the base URL is
/// injectable for tests).</summary>
public sealed class HttpOllamaRegistry : IOllamaRegistry, IDisposable
{
    public const string DefaultBaseUrl = "https://registry.ollama.ai";

    /// <summary>Tag lists are NOT served by the registry host (its OCI <c>/v2/…/tags/list</c> 404s) —
    /// they come from the website: <c>https://ollama.com/&lt;ns&gt;/&lt;model&gt;/tags</c> with
    /// <c>Accept: application/json</c> returns <c>{"tags":[…]}</c>.</summary>
    public const string DefaultTagsBaseUrl = "https://ollama.com";

    private readonly string _baseUrl;
    private readonly string _tagsBaseUrl;
    private readonly HttpClient _http;

    public HttpOllamaRegistry(string? baseUrl = null, HttpClient? http = null, string? tagsBaseUrl = null)
    {
        _baseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
        _tagsBaseUrl = (tagsBaseUrl ?? DefaultTagsBaseUrl).TrimEnd('/');
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<OllamaManifest> GetManifestAsync(OllamaModelRef model, CancellationToken ct)
    {
        var url = $"{_baseUrl}/v2/{model.PathNamespaceModel}/manifests/{model.Tag}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Accept", "application/vnd.docker.distribution.manifest.v2+json");

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException(
                $"Model '{model}' was not found in the Ollama registry — check the name and tag.");
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"The Ollama registry is unreachable or returned an error ({(int)resp.StatusCode}) for '{model}'.");

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return OllamaManifest.Parse(json);
    }

    public async Task<IReadOnlyList<string>> GetTagsAsync(OllamaModelRef model, CancellationToken ct)
    {
        var url = $"{_tagsBaseUrl}/{model.PathNamespaceModel}/tags";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return Array.Empty<string>(); // no tag list is not an error — the direct resolve still works
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        return tags.EnumerateArray()
            .Select(t => t.GetString())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!)
            .ToList();
    }

    public string BlobUrl(OllamaModelRef model, string digest) =>
        $"{_baseUrl}/v2/{model.PathNamespaceModel}/blobs/{digest}";

    public async Task DownloadBlobAsync(OllamaModelRef model, string digest, string destinationPath, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(BlobUrl(model, digest), HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var tmp = destinationPath + ".tmp";
        await using (var fs = File.Create(tmp))
            await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
        File.Move(tmp, destinationPath, overwrite: true);
    }

    public void Dispose() => _http.Dispose();
}
