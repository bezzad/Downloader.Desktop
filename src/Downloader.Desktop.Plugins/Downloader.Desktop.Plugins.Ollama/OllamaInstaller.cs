using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Downloader.Desktop.Plugins.Ollama;

/// <summary>
/// Installs a downloaded model blob into the local Ollama store ("Add to Ollama"): verifies the file's
/// sha256 against the manifest's model layer, fetches the small metadata layers, hard-links (or copies)
/// the blobs into <c>{store}/blobs</c>, and writes the manifest LAST so Ollama never sees a half-installed
/// model. Never modifies the user's downloaded file.
/// </summary>
public sealed class OllamaInstaller
{
    private readonly IOllamaRegistry _registry;

    public OllamaInstaller(IOllamaRegistry registry) => _registry = registry;

    /// <summary>The local model store root: $OLLAMA_MODELS, else ~/.ollama/models.</summary>
    public static string DefaultStoreRoot()
    {
        var env = Environment.GetEnvironmentVariable("OLLAMA_MODELS");
        if (!string.IsNullOrWhiteSpace(env))
            return env;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models");
    }

    /// <summary>Runs the install. <paramref name="storeRoot"/> is injectable for tests (null → default).</summary>
    public async Task InstallAsync(OllamaModelRef model, string filePath, string? storeRoot,
        IProgress<double>? progress, CancellationToken ct)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("The downloaded model file no longer exists.", filePath);

        storeRoot ??= DefaultStoreRoot();
        // ~/.ollama missing entirely ⇒ Ollama isn't set up on this machine (a custom OLLAMA_MODELS is
        // trusted as-is and created if needed).
        var defaultRoot = Environment.GetEnvironmentVariable("OLLAMA_MODELS") == null;
        if (defaultRoot && !Directory.Exists(Path.GetDirectoryName(storeRoot)!))
            throw new InvalidOperationException(
                "Ollama doesn't appear to be installed (no ~/.ollama folder). Install Ollama first, then try again.");

        progress?.Report(0.05);
        var manifest = await _registry.GetManifestAsync(model, ct).ConfigureAwait(false);
        var modelLayer = manifest.ModelLayer!;

        // 1) Verify the downloaded bytes ARE the manifest's model layer (streamed sha256).
        var actual = await Sha256OfFileAsync(filePath, ct).ConfigureAwait(false);
        var expected = modelLayer.Digest; // "sha256:<hex>"
        if (!string.Equals(expected, $"sha256:{actual}", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Checksum mismatch — the downloaded file is not '{model}' (expected {expected}, got sha256:{actual}). " +
                "Re-download the model and try again.");
        progress?.Report(0.5);

        // 2) Blobs: the model blob links/copies from the downloaded file; metadata layers come from the registry.
        var blobsDir = Path.Combine(storeRoot, "blobs");
        Directory.CreateDirectory(blobsDir);
        HardLinkOrCopy(filePath, Path.Combine(blobsDir, BlobFileName(modelLayer.Digest)));

        var metadata = manifest.MetadataLayers.Where(l => !string.IsNullOrEmpty(l.Digest)).ToList();
        for (var i = 0; i < metadata.Count; i++)
        {
            var blobPath = Path.Combine(blobsDir, BlobFileName(metadata[i].Digest));
            if (!File.Exists(blobPath))
                await _registry.DownloadBlobAsync(model, metadata[i].Digest, blobPath, ct).ConfigureAwait(false);
            progress?.Report(0.5 + 0.4 * (i + 1) / metadata.Count);
        }

        // 3) Manifest LAST — its presence is what makes the model appear in `ollama list`.
        var manifestPath = Path.Combine(storeRoot, "manifests", "registry.ollama.ai",
            model.Namespace, model.Model, model.Tag);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        var tmp = manifestPath + ".tmp";
        await File.WriteAllTextAsync(tmp, manifest.RawJson, ct).ConfigureAwait(false);
        File.Move(tmp, manifestPath, overwrite: true);
        progress?.Report(1.0);
    }

    /// <summary>"sha256:<hex>" → "sha256-<hex>" (the store's blob file naming).</summary>
    public static string BlobFileName(string digest) => digest.Replace(':', '-');

    public static async Task<string> Sha256OfFileAsync(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Hard-links when the OS/filesystem allows (no extra disk for multi-GB models), else copies.
    /// The source file is never modified either way.</summary>
    public static void HardLinkOrCopy(string source, string destination)
    {
        if (File.Exists(destination))
            return; // blob already in the store
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (!CreateHardLink(destination, source, IntPtr.Zero))
                    throw new IOException("CreateHardLink failed");
            }
            else if (link(source, destination) != 0)
            {
                throw new IOException("link() failed");
            }
        }
        catch
        {
            File.Copy(source, destination, overwrite: false);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    [DllImport("libc", SetLastError = true)]
    private static extern int link(string oldpath, string newpath);
}
