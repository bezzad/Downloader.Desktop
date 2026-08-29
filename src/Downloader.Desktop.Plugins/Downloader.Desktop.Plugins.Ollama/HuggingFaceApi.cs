using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Downloader.Desktop.Plugins.Ollama;

/// <summary>One file in a HuggingFace repository, as its API lists it. <paramref name="Sha256"/> is the
/// LFS object id — the digest the repository publishes for the file's contents, and the only integrity
/// check available for a HuggingFace download (unlike an Ollama model, there is no manifest to compare
/// against). It is null for small non-LFS files, which model weights never are.</summary>
public sealed record HuggingFaceFile(string Path, long Size, string? Sha256)
{
    /// <summary>The file name without its directory.</summary>
    public string Name => Path.Contains('/') ? Path[(Path.LastIndexOf('/') + 1)..] : Path;

    /// <summary>Is this a GGUF model file — the format Ollama runs?</summary>
    public bool IsGguf => Name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase);

    /// <summary>The quantisation as the community names it (<c>Q4_K_M</c> in
    /// <c>Qwen3-8B-Q4_K_M.gguf</c>), or the bare file name when it carries no recognisable suffix. This is
    /// what a person actually chooses between, so it is what the variant is labelled and named with.</summary>
    public string Quantisation
    {
        get
        {
            var stem = Name[..^".gguf".Length];
            var dash = stem.LastIndexOf('-');
            var candidate = dash >= 0 && dash < stem.Length - 1 ? stem[(dash + 1)..] : stem;
            return candidate.Length == 0 ? stem : candidate;
        }
    }

    /// <summary>Part of a multi-file (sharded) GGUF set, e.g. <c>model-00001-of-00003.gguf</c>. Ollama
    /// cannot be handed one shard, so these are refused with an explanation rather than half-downloaded.</summary>
    public bool IsShard => ShardPattern.IsMatch(Name);

    private static readonly System.Text.RegularExpressions.Regex ShardPattern =
        new(@"-\d{3,5}-of-\d{3,5}\.gguf$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}

/// <summary>Reading a HuggingFace repository's file list, behind an interface so every decision built on
/// it is testable without the network.</summary>
public interface IHuggingFaceApi
{
    /// <summary>The repository's files at the given revision. Throws
    /// <see cref="HuggingFaceRepoException"/> with a message naming which problem it was when the
    /// repository does not exist or cannot be read without credentials.</summary>
    Task<IReadOnlyList<HuggingFaceFile>> ListFilesAsync(HuggingFaceModelRef model, CancellationToken ct);
}

/// <summary>A repository that could not be listed, worded for the user: the two cases people hit — it is
/// not there, or it is gated — are told apart, because the fix differs.</summary>
public sealed class HuggingFaceRepoException : Exception
{
    public HuggingFaceRepoException(string message) : base(message) { }
}

/// <summary>The real <see cref="IHuggingFaceApi"/>, over HuggingFace's public repository-tree API.</summary>
public sealed class HttpHuggingFaceApi : IHuggingFaceApi
{
    private readonly HttpClient _http;
    private readonly string _apiBase;

    public HttpHuggingFaceApi(HttpClient? http = null, string apiBase = "https://huggingface.co/api")
    {
        _http = http ?? new HttpClient();
        _apiBase = apiBase.TrimEnd('/');
    }

    public async Task<IReadOnlyList<HuggingFaceFile>> ListFilesAsync(
        HuggingFaceModelRef model, CancellationToken ct)
    {
        var url = $"{_apiBase}/models/{model.Owner}/{model.Repo}/tree/{model.Revision}?recursive=true";
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new HuggingFaceRepoException(
                $"Couldn't reach HuggingFace to read '{model.RepoId}'. Check your connection and try again.");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.NotFound)
                throw new HuggingFaceRepoException(
                    $"The HuggingFace repository '{model.RepoId}' doesn't exist (or was renamed).");
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new HuggingFaceRepoException(
                    $"The HuggingFace repository '{model.RepoId}' is private or gated — it can only be "
                    + "downloaded by an account that has been granted access.");
            if (!response.IsSuccessStatusCode)
                throw new HuggingFaceRepoException(
                    $"HuggingFace returned {(int)response.StatusCode} for '{model.RepoId}'.");

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Parse(json);
        }
    }

    /// <summary>Reads the tree JSON. Pure, so the shape HuggingFace returns is pinned by tests: a file's
    /// real size and digest live under <c>lfs</c> for anything big enough to be model weights.</summary>
    internal static IReadOnlyList<HuggingFaceFile> Parse(string json)
    {
        var files = new List<HuggingFaceFile>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return files;

        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                continue;
            if (entry.TryGetProperty("type", out var type) && type.GetString() != "file")
                continue;
            if (!entry.TryGetProperty("path", out var pathEl) || pathEl.GetString() is not { } path)
                continue;

            long size = entry.TryGetProperty("size", out var sizeEl) && sizeEl.TryGetInt64(out var s) ? s : 0;
            string? sha = null;
            if (entry.TryGetProperty("lfs", out var lfs) && lfs.ValueKind == JsonValueKind.Object)
            {
                if (lfs.TryGetProperty("oid", out var oid))
                    sha = oid.GetString();
                if (lfs.TryGetProperty("size", out var lfsSize) && lfsSize.TryGetInt64(out var ls) && ls > 0)
                    size = ls; // the real object size; the tree's own "size" is the pointer file's
            }

            files.Add(new HuggingFaceFile(path, size, Normalize(sha)));
        }

        return files;
    }

    /// <summary>LFS oids are bare hex; a digest that arrives prefixed is accepted too.</summary>
    private static string? Normalize(string? sha)
    {
        if (string.IsNullOrWhiteSpace(sha))
            return null;
        var value = sha.Trim();
        if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            value = value["sha256:".Length..];
        return value.Length == 64 ? value.ToLowerInvariant() : null;
    }
}
