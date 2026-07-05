using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Downloader.Desktop.Plugins;

namespace Downloader.Desktop.Plugins.GitHub;

/// <summary>
/// THE TEMPLATE PLUGIN — copy this to start your own. It implements ALL plugin interfaces so you can see
/// each one in a real (small) context:
///   * IDownloaderPlugin  — the entry point (id/name/version + Initialize).
///   * ILinkResolver     — "GitHub Releases": paste github.com/owner/repo and it resolves the latest
///                          release's download URL for your OS (the headline, genuinely-useful feature).
///   * IPostProcessor     — writes a ".sha256" checksum sidecar after a download (demo).
///   * ITransferProvider  — a tiny "file://" copier that OWNS its transfer (demo, like a torrent plugin).
/// The core engine still does the actual HTTP downloading — resolvers only return URLs.
/// </summary>
public sealed class GitHubReleasesPlugin : IDownloaderPlugin
{
    public string Id => "com.bezzad.github-releases";
    public string Name => "GitHub Releases";
    public string Version => "1.0.0";
    public string Author => "bezzad";
    public string Description => "Paste a github.com/owner/repo link to download the latest release asset for your OS.";

    public void Initialize(IPluginContext context)
    {
        context.Logger.LogInformation("GitHub Releases plugin initialized");
        context.RegisterResolver(new GitHubReleasesResolver());
        context.RegisterPostProcessor(new Sha256SidecarPostProcessor());
        context.RegisterTransferProvider(new LocalFileTransferProvider());
    }
}

// -- RESOLVE: github.com/owner/repo  ->  the latest release asset URL ---------------------------------
internal sealed class GitHubReleasesResolver : ILinkResolver
{
    private static readonly HttpClient Http = CreateClient();

    public bool CanResolve(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && u.AbsolutePath.Trim('/').Split('/').Length >= 2; // owner/repo

    public async Task<DownloadPlan> ResolveAsync(string url, CancellationToken cancellationToken)
    {
        var parts = new Uri(url).AbsolutePath.Trim('/').Split('/');
        var (owner, repo) = (parts[0], parts[1]);

        using var resp = await Http.GetAsync(
            $"https://api.github.com/repos/{owner}/{repo}/releases/latest", cancellationToken)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var assets = doc.RootElement.GetProperty("assets");

        // Prefer an asset whose name matches the running OS; otherwise just take the first.
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
               : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "linux";
        JsonElement? chosen = null;
        foreach (var a in assets.EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            if (name.Contains(os, StringComparison.OrdinalIgnoreCase)) { chosen = a; break; }
            chosen ??= a;
        }
        if (chosen is null)
            throw new InvalidOperationException("The latest release has no downloadable assets.");

        var asset = chosen.Value;
        return new DownloadPlan
        {
            SuggestedFileName = asset.GetProperty("name").GetString(),
            Parts = new[]
            {
                new DownloadPart
                {
                    Url = asset.GetProperty("browser_download_url").GetString()!,
                    Kind = PartKind.Combined,
                    ExpectedSize = asset.TryGetProperty("size", out var s) ? s.GetInt64() : null,
                },
            },
            PostProcess = PostProcess.None, // a single file -> nothing to combine
        };
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient();
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Downloader-Plugin", "1.0")); // GitHub requires a UA
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }
}

// -- POST-PROCESS: write a ".sha256" sidecar next to the downloaded file (demo) ----------------------
internal sealed class Sha256SidecarPostProcessor : IPostProcessor
{
    public bool CanProcess(PostProcess plan) => plan.Kind == PostProcessKind.Decrypt; // demo trigger

    public async Task<string> ProcessAsync(IReadOnlyList<string> inputFiles, PostProcess plan, string outputPath,
        IProgress<double> progress, CancellationToken cancellationToken)
    {
        var file = inputFiles.Count > 0 ? inputFiles[0] : outputPath;
        await using (var fs = File.OpenRead(file))
        {
            var hash = await SHA256.HashDataAsync(fs, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(file + ".sha256", Convert.ToHexString(hash).ToLowerInvariant(),
                cancellationToken).ConfigureAwait(false);
        }
        progress.Report(100);
        return file;
    }
}

// -- TRANSFER: a plugin that OWNS the whole download (here: copy a local file://). Shows how a
//    torrent/HLS transfer plugin plugs in -- it doesn't return a URL, it fetches the bytes itself. ----
internal sealed class LocalFileTransferProvider : ITransferProvider
{
    public bool CanHandle(string url) => url.StartsWith("file://", StringComparison.OrdinalIgnoreCase);
    public ITransfer Create(string url, string targetFolder) => new LocalFileTransfer(url, targetFolder);
}

internal sealed class LocalFileTransfer(string url, string targetFolder) : ITransfer
{
    public event EventHandler<TransferProgress>? ProgressChanged;

    public async Task<string> StartAsync(CancellationToken cancellationToken)
    {
        var src = new Uri(url).LocalPath;
        Directory.CreateDirectory(targetFolder);
        var dest = Path.Combine(targetFolder, Path.GetFileName(src));
        var total = new FileInfo(src).Length;

        await using var input = File.OpenRead(src);
        await using var output = File.Create(dest);
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            copied += read;
            ProgressChanged?.Invoke(this, new TransferProgress
            {
                BytesReceived = copied, TotalBytes = total,
                Percentage = total > 0 ? copied * 100.0 / total : 0,
            });
        }
        return dest;
    }

    public void Pause() { /* a real transfer would suspend here */ }
    public void Resume() { /* ...and resume here */ }
}
