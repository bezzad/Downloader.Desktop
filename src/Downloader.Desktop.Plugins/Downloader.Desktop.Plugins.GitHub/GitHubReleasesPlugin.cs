using System.Collections.Concurrent;
using System.Net;
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
///   * ILinkResolver     — "GitHub Releases": paste a repository, a releases page or one release and
///                          it offers that release's assets, with your platform's pre-selected.
///   * IPostProcessor     — writes a ".sha256" checksum sidecar after a download (demo).
///   * ITransferProvider  — a tiny "file://" copier that OWNS its transfer (demo, like a torrent plugin).
/// The core engine still does the actual HTTP downloading — resolvers only return URLs.
/// </summary>
public sealed class GitHubReleasesPlugin : IDownloaderPlugin
{
    public string Id => "com.bezzad.github-releases";
    public string Name => "GitHub Releases";
    public string Version => "1.1.0";
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

// -- RESOLVE: a github.com link -> the file it actually names -----------------------------------------

/// <summary>What kind of GitHub link this is. The plugin claims a link ONLY when it can produce a better
/// download than the link itself; anything else is left to the app, which downloads the address as given.
/// A resolver that claims a link it cannot improve is worse than one that stays out of the way — it
/// silently substitutes a different file.</summary>
internal enum GitHubLinkKind
{
    /// <summary>Not ours: another host, an issue/PR/wiki/tree page, or a link that already IS the file.</summary>
    NotClaimed,
    /// <summary>A repository or its releases list: the newest release.</summary>
    LatestRelease,
    /// <summary>A named release: <c>/releases/tag/&lt;tag&gt;</c>, or the <c>#release-&lt;tag&gt;</c> anchor
    /// GitHub puts on each entry of its own releases page.</summary>
    TaggedRelease,
    /// <summary>A file held in the repository (<c>/blob/</c> or <c>/raw/</c>): the file's own bytes.</summary>
    RawFile,
}

/// <summary>One reading of a GitHub URL, shared by <c>CanResolve</c>, <c>GetVariantsAsync</c> and
/// <c>ResolveAsync</c>. They used to parse the string separately and disagree: the claim accepted any
/// <c>github.com/&lt;owner&gt;/&lt;repo&gt;/…</c> path while the resolve always asked for the LATEST
/// release, so an issue link, a file link and a link to v2.9.0's asset all downloaded v2.10.0's asset.</summary>
internal sealed record GitHubLink(
    GitHubLinkKind Kind, string Owner = "", string Repo = "", string? Tag = null,
    string? Ref = null, string? Path = null)
{
    internal static readonly GitHubLink None = new(GitHubLinkKind.NotClaimed);

    /// <summary>Pure and total: anything unrecognised is <see cref="GitHubLinkKind.NotClaimed"/>.</summary>
    internal static GitHubLink Parse(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return None;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return None;
        var host = uri.Host.TrimStart();
        if (!host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && !host.Equals("www.github.com", StringComparison.OrdinalIgnoreCase))
            return None;

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return None; // an owner alone owns no downloadable thing

        var owner = Uri.UnescapeDataString(segments[0]);
        var repo = Uri.UnescapeDataString(segments[1]);
        if (segments.Length == 2)
            return new GitHubLink(GitHubLinkKind.LatestRelease, owner, repo);

        switch (segments[2].ToLowerInvariant())
        {
            case "releases":
                // /releases            -> latest, unless the page anchor names one entry
                // /releases/latest     -> latest
                // /releases/tag/<tag>  -> that release
                // /releases/download/… -> already the asset itself: not ours
                if (segments.Length == 3)
                {
                    var anchored = TagFromFragment(uri.Fragment);
                    return anchored is null
                        ? new GitHubLink(GitHubLinkKind.LatestRelease, owner, repo)
                        : new GitHubLink(GitHubLinkKind.TaggedRelease, owner, repo, anchored);
                }
                if (segments[3].Equals("latest", StringComparison.OrdinalIgnoreCase))
                    return new GitHubLink(GitHubLinkKind.LatestRelease, owner, repo);
                if (segments[3].Equals("tag", StringComparison.OrdinalIgnoreCase) && segments.Length >= 5)
                    return new GitHubLink(GitHubLinkKind.TaggedRelease, owner, repo,
                        Uri.UnescapeDataString(segments[4]));
                return None;

            case "blob":
            case "raw":
                // /blob/<ref>/<path…> — the page that DISPLAYS a file; the file itself lives on
                // raw.githubusercontent.com.
                if (segments.Length < 5)
                    return None;
                return new GitHubLink(GitHubLinkKind.RawFile, owner, repo,
                    Ref: Uri.UnescapeDataString(segments[3]),
                    Path: string.Join('/', segments.Skip(4).Select(Uri.UnescapeDataString)));

            default:
                return None; // issues, pull, discussions, wiki, tree, commit, actions, …
        }
    }

    /// <summary>GitHub links each entry of its releases page as <c>#release-&lt;tag&gt;</c>. Anything else
    /// is ignored rather than guessed at, leaving the link meaning "latest".</summary>
    private static string? TagFromFragment(string fragment)
    {
        var value = fragment.TrimStart('#');
        if (!value.StartsWith("release-", StringComparison.OrdinalIgnoreCase))
            return null;
        var tag = Uri.UnescapeDataString(value["release-".Length..]);
        return string.IsNullOrWhiteSpace(tag) ? null : tag;
    }
}

/// <summary>One downloadable file of a release.</summary>
internal sealed record GitHubAsset(string Id, string Name, string Url, long? Size);

internal sealed record GitHubRelease(string Tag, IReadOnlyList<GitHubAsset> Assets);

internal sealed class GitHubReleasesResolver : ILinkResolver
{
    private static readonly HttpClient Http = CreateClient();

    /// <summary>The Add window lists a link's assets and then resolves the pick, so the same release would
    /// be fetched twice — against an anonymous rate limit of 60 requests an hour. Short-lived by design:
    /// long enough to serve one user action, not long enough to hide a new release.</summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, (DateTime FetchedUtc, GitHubRelease Release)> Cache = new();

    /// <summary>Where the releases API lives. Overridden by tests with a loopback server, so the asset
    /// listing, the tag lookup and the failure wording are all exercised without the network (and without
    /// spending the anonymous rate limit on every test run).</summary>
    internal static string ApiBase { get; set; } = "https://api.github.com";

    /// <summary>Drops the release cache; tests set a new stub between cases.</summary>
    internal static void ClearCache() => Cache.Clear();

    public bool CanResolve(string url) => GitHubLink.Parse(url).Kind != GitHubLinkKind.NotClaimed;

    public async Task<IReadOnlyList<LinkVariant>?> GetVariantsAsync(
        string url, ResolveOptions? options, CancellationToken cancellationToken)
    {
        var link = GitHubLink.Parse(url);
        if (link.Kind is GitHubLinkKind.NotClaimed or GitHubLinkKind.RawFile)
            return null; // nothing to choose between: it is one file, or not ours at all

        var release = await GetReleaseAsync(link, cancellationToken).ConfigureAwait(false);
        if (release.Assets.Count == 0)
            return null; // the failure belongs to the resolve, which can explain it properly

        var best = PickForThisMachine(release.Assets);
        return release.Assets.Select(a => new LinkVariant
        {
            Id = a.Id,
            Label = a.Name,
            Description = a.Size is > 0 ? DescribeSize(a.Size.Value) : null,
            ExpectedSize = a.Size,
            IsDefault = ReferenceEquals(a, best),
            // A release asset IS its own link, so the download carries the asset's address rather than the
            // pasted page plus a variant id — which is also why a retry re-fetches that exact asset.
            SubstituteUrl = a.Url,
        }).ToList();
    }

    public Task<DownloadPlan> ResolveAsync(string url, CancellationToken cancellationToken)
        => ResolveAsync(url, null, cancellationToken);

    public async Task<DownloadPlan> ResolveAsync(string url, ResolveOptions? options, CancellationToken cancellationToken)
    {
        var link = GitHubLink.Parse(url);
        if (link.Kind == GitHubLinkKind.NotClaimed)
            throw new InvalidOperationException("This GitHub link does not point at a release or a file.");

        if (link.Kind == GitHubLinkKind.RawFile)
        {
            var raw = $"https://raw.githubusercontent.com/{link.Owner}/{link.Repo}/{link.Ref}/{link.Path}";
            return new DownloadPlan
            {
                SuggestedFileName = link.Path![(link.Path!.LastIndexOf('/') + 1)..],
                Parts = new[] { new DownloadPart { Url = raw, Kind = PartKind.Combined } },
                PostProcess = PostProcess.None,
            };
        }

        var release = await GetReleaseAsync(link, cancellationToken).ConfigureAwait(false);
        if (release.Assets.Count == 0)
            throw new InvalidOperationException(
                $"The release {release.Tag} has no downloadable files attached.");

        // A host that hands the chosen id back (rather than using the variant's own address) still gets
        // the asset it asked for; an unknown id falls back to this machine's asset rather than failing.
        var chosen = FindById(release.Assets, options?.VariantId) ?? PickForThisMachine(release.Assets);
        return new DownloadPlan
        {
            SuggestedFileName = chosen.Name,
            Parts = new[]
            {
                new DownloadPart { Url = chosen.Url, Kind = PartKind.Combined, ExpectedSize = chosen.Size },
            },
            PostProcess = PostProcess.None, // a single file -> nothing to combine
        };
    }

    private static GitHubAsset? FindById(IReadOnlyList<GitHubAsset> assets, string? variantId)
    {
        if (string.IsNullOrWhiteSpace(variantId))
            return null;
        return assets.FirstOrDefault(a => a.Id == variantId)
            ?? assets.FirstOrDefault(a => a.Name.Equals(variantId, StringComparison.OrdinalIgnoreCase));
    }

    private static GitHubAsset PickForThisMachine(IReadOnlyList<GitHubAsset> assets) =>
        PickAsset(assets, CurrentOs(), CurrentArchitecture());

    internal static string CurrentOs() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos"
        : "linux";

    internal static string CurrentArchitecture() =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

    /// <summary>Which asset belongs to a given machine — pure, so the Windows and macOS answers are
    /// testable from any box. Deliberately more than <c>name.Contains("win")</c>: that also matches
    /// <c>darwin</c>. When several assets fit the OS, the one matching the processor wins (this is what
    /// separates <c>osx-arm64</c> from <c>osx-x64</c>); with no OS match at all, the first asset is used,
    /// which is what the plugin has always done.</summary>
    internal static GitHubAsset PickAsset(IReadOnlyList<GitHubAsset> assets, string os, string architecture)
    {
        GitHubAsset? osMatch = null;
        foreach (var asset in assets)
        {
            if (!MatchesOs(asset.Name, os))
                continue;
            if (MatchesArchitecture(asset.Name, architecture))
                return asset; // the best possible fit; nothing later can beat it
            osMatch ??= asset;
        }
        return osMatch ?? assets[0];
    }

    private static bool MatchesOs(string name, string os)
    {
        var lower = name.ToLowerInvariant();
        return os switch
        {
            "windows" => lower.Contains("windows") || (lower.Contains("win") && !lower.Contains("darwin")),
            "macos" => lower.Contains("osx") || lower.Contains("macos") || lower.Contains("darwin")
                       || lower.Contains("mac") || lower.Contains("apple"),
            _ => lower.Contains("linux"),
        };
    }

    private static bool MatchesArchitecture(string name, string architecture)
    {
        var lower = name.ToLowerInvariant();
        return architecture == "arm64"
            ? lower.Contains("arm64") || lower.Contains("aarch64")
            : lower.Contains("x64") || lower.Contains("amd64") || lower.Contains("x86_64");
    }

    internal static string DescribeSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    private static async Task<GitHubRelease> GetReleaseAsync(GitHubLink link, CancellationToken cancellationToken)
    {
        var api = link.Kind == GitHubLinkKind.TaggedRelease
            ? $"{ApiBase}/repos/{link.Owner}/{link.Repo}/releases/tags/{Uri.EscapeDataString(link.Tag!)}"
            : $"{ApiBase}/repos/{link.Owner}/{link.Repo}/releases/latest";

        if (Cache.TryGetValue(api, out var cached) && DateTime.UtcNow - cached.FetchedUtc < CacheFor)
            return cached.Release;

        using var response = await Http.GetAsync(api, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(DescribeApiFailure(link, response.StatusCode));

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var assets = new List<GitHubAsset>();
        if (doc.RootElement.TryGetProperty("assets", out var assetArray))
        {
            foreach (var a in assetArray.EnumerateArray())
            {
                var downloadUrl = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(name))
                    continue;
                assets.Add(new GitHubAsset(
                    Id: a.TryGetProperty("id", out var id) ? id.ToString() : name!,
                    Name: name!,
                    Url: downloadUrl!,
                    Size: a.TryGetProperty("size", out var s) && s.TryGetInt64(out var size) && size > 0 ? size : null));
            }
        }

        var tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        var release = new GitHubRelease(tag ?? link.Tag ?? "latest", assets);
        Cache[api] = (DateTime.UtcNow, release);
        return release;
    }

    /// <summary>A sentence the user can act on. A claiming resolver's failure reaches the row verbatim, so
    /// "Response status code does not indicate success: 404" would be the whole explanation.</summary>
    private static string DescribeApiFailure(GitHubLink link, HttpStatusCode status) => status switch
    {
        HttpStatusCode.NotFound when link.Kind == GitHubLinkKind.TaggedRelease =>
            $"{link.Owner}/{link.Repo} has no release tagged {link.Tag}.",
        HttpStatusCode.NotFound =>
            $"{link.Owner}/{link.Repo} has no published releases (or the repository is private).",
        HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests =>
            "GitHub is rate-limiting this machine right now. Try again in a few minutes.",
        _ => $"GitHub answered {(int)status} for {link.Owner}/{link.Repo}.",
    };

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
