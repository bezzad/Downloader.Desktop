using System.Text.Json;

namespace Downloader.Desktop.Plugins.Ollama;

/// <summary>
/// Installs a downloaded HuggingFace GGUF into the local Ollama store, so it shows up in
/// <c>ollama list</c> under <c>hf.co/&lt;owner&gt;/&lt;repo&gt;:&lt;quantisation&gt;</c> — the same naming
/// Ollama itself uses when it pulls from HuggingFace, so a model installed here and one pulled there are
/// the same entry rather than two.
/// <para>
/// The difference from an ollama.com model is what integrity can be checked against: there is no registry
/// manifest, so the file is verified against the digest the REPOSITORY publishes for it. When the
/// repository publishes none, nothing is invented — the install proceeds and says so, because refusing a
/// download that is fine would be worse than the check being unavailable.
/// </para>
/// The user's downloaded file is never moved or deleted; the blob is hard-linked where the filesystem
/// allows and copied otherwise.
/// </summary>
public sealed class HuggingFaceInstaller
{
    /// <summary>Where these models live in the store's manifest tree — Ollama's own naming for a model
    /// that came from HuggingFace.</summary>
    public const string RegistryFolder = "hf.co";

    /// <summary>Runs the install. <paramref name="storeRoot"/> is injectable for tests (null → default).</summary>
    public async Task InstallAsync(HuggingFaceModelRef model, string filePath, string? expectedSha256,
        string? storeRoot, IProgress<double>? progress, CancellationToken ct)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("The downloaded model file no longer exists.", filePath);

        storeRoot ??= OllamaInstaller.DefaultStoreRoot();
        // ~/.ollama missing entirely ⇒ Ollama isn't set up on this machine (a custom OLLAMA_MODELS is
        // trusted as-is and created if needed).
        var defaultRoot = Environment.GetEnvironmentVariable("OLLAMA_MODELS") == null;
        if (defaultRoot && !Directory.Exists(Path.GetDirectoryName(storeRoot)!))
            throw new InvalidOperationException(
                "Ollama doesn't appear to be installed (no ~/.ollama folder). Install Ollama first, then "
                + $"try again. Looked in: {storeRoot}");

        progress?.Report(0.05);

        // 1) The file must be what the repository published, when the repository published a digest.
        var actual = await OllamaInstaller.Sha256OfFileAsync(filePath, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(expectedSha256)
            && !string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Checksum mismatch — the downloaded file is not the one '{model.RepoId}' publishes "
                + $"(expected sha256:{expectedSha256}, got sha256:{actual}). Nothing was added to Ollama; "
                + "re-download the model and try again.");
        progress?.Report(0.5);

        // 2) Blobs: the model itself, plus the tiny config blob a manifest must point at.
        var blobsDir = Path.Combine(storeRoot, "blobs");
        Directory.CreateDirectory(blobsDir);

        var modelDigest = $"sha256:{actual}";
        OllamaInstaller.HardLinkOrCopy(filePath, Path.Combine(blobsDir, OllamaInstaller.BlobFileName(modelDigest)));

        var configJson = ConfigJson();
        var configBytes = System.Text.Encoding.UTF8.GetBytes(configJson);
        var configDigest = "sha256:" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(configBytes)).ToLowerInvariant();
        var configPath = Path.Combine(blobsDir, OllamaInstaller.BlobFileName(configDigest));
        if (!File.Exists(configPath))
            await File.WriteAllBytesAsync(configPath, configBytes, ct).ConfigureAwait(false);
        progress?.Report(0.8);

        // 3) Manifest LAST — its presence is what makes the model appear in `ollama list`, so a crash
        // before this point leaves nothing half-registered.
        var size = new FileInfo(filePath).Length;
        var manifest = ManifestJson(modelDigest, size, configDigest, configBytes.Length);
        var manifestPath = ManifestPath(storeRoot, model, Tag(filePath));
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        var tmp = manifestPath + ".tmp";
        await File.WriteAllTextAsync(tmp, manifest, ct).ConfigureAwait(false);
        File.Move(tmp, manifestPath, overwrite: true);
        progress?.Report(1.0);
    }

    /// <summary>Where this model's manifest goes: <c>{store}/manifests/hf.co/{owner}/{repo}/{tag}</c>.</summary>
    public static string ManifestPath(string storeRoot, HuggingFaceModelRef model, string tag) =>
        Path.Combine(storeRoot, "manifests", RegistryFolder, model.Owner.ToLowerInvariant(),
            model.Repo.ToLowerInvariant(), tag);

    /// <summary>The name the model gets in <c>ollama list</c>, e.g.
    /// <c>hf.co/unsloth/Qwen3-8B-GGUF:Q4_K_M</c>.</summary>
    public static string LocalModelName(HuggingFaceModelRef model, string filePath) =>
        $"{RegistryFolder}/{model.Owner}/{model.Repo}:{Tag(filePath)}";

    /// <summary>The tag is the quantisation, which is the only thing that distinguishes two downloads of
    /// the same repository — lowercased, because Ollama's model names are.</summary>
    internal static string Tag(string filePath)
    {
        var name = Path.GetFileName(filePath);
        var quant = new HuggingFaceFile(name, 0, null).Quantisation;
        var cleaned = new string(quant.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '-').ToArray());
        return cleaned.Length == 0 ? "latest" : cleaned.ToLowerInvariant();
    }

    /// <summary>A minimal Ollama image config. It carries no template or parameters — a GGUF file already
    /// embeds its own — so this exists to satisfy the manifest's config reference and nothing more.</summary>
    internal static string ConfigJson() => JsonSerializer.Serialize(new
    {
        model_format = "gguf",
        model_family = "",
        model_type = "",
        file_type = "",
        architecture = "amd64",
        os = "linux",
        rootfs = new { type = "layers", diff_ids = Array.Empty<string>() },
    });

    internal static string ManifestJson(string modelDigest, long modelSize, string configDigest, long configSize) =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = 2,
            mediaType = "application/vnd.docker.distribution.manifest.v2+json",
            config = new
            {
                mediaType = "application/vnd.docker.container.image.v1+json",
                digest = configDigest,
                size = configSize,
            },
            layers = new[]
            {
                new
                {
                    mediaType = OllamaManifest.ModelMediaType,
                    digest = modelDigest,
                    size = modelSize,
                },
            },
        });
}

/// <summary>
/// The "Add to Ollama" action for a completed HuggingFace model download. It is a second action rather
/// than a branch inside <see cref="AddToOllamaAction"/> because the two have nothing in common but their
/// label: different address to parse, different integrity source, different place in the store. They
/// claim disjoint URLs, so the host offers exactly one of them.
/// </summary>
public sealed class AddHuggingFaceToOllamaAction : IPostDownloadAction
{
    private readonly IHuggingFaceApi _api;

    public AddHuggingFaceToOllamaAction(IHuggingFaceApi api) => _api = api;

    /// <summary>Test seam: overrides the store root (null → $OLLAMA_MODELS / ~/.ollama/models).</summary>
    public string? StoreRootOverride { get; set; }

    public string Label => "Add to Ollama";

    public bool CanOffer(string sourceUrl, string filePath) =>
        HuggingFaceModelRef.TryParse(sourceUrl, out _)
        && !string.IsNullOrWhiteSpace(filePath)
        && filePath.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
        && File.Exists(filePath);

    public async Task ExecuteAsync(string sourceUrl, string filePath, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (!HuggingFaceModelRef.TryParse(sourceUrl, out var model))
            throw new InvalidOperationException($"'{sourceUrl}' is not a HuggingFace model repository.");

        // Ask the repository what it published for this exact file. Doing it here rather than remembering
        // it from resolve time keeps the check honest across a restart, where the download record survives
        // but nothing else does.
        var expected = await ExpectedDigestAsync(model!, filePath, cancellationToken).ConfigureAwait(false);

        await new HuggingFaceInstaller()
            .InstallAsync(model!, filePath, expected, StoreRootOverride, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The digest the repository publishes for the downloaded file, or null when it publishes
    /// none (or cannot be reached — an install must not fail because the check was unavailable).</summary>
    private async Task<string?> ExpectedDigestAsync(HuggingFaceModelRef model, string filePath, CancellationToken ct)
    {
        try
        {
            var files = await _api.ListFilesAsync(model, ct).ConfigureAwait(false);
            var name = Path.GetFileName(filePath);
            return files.FirstOrDefault(f =>
                string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))?.Sha256;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
