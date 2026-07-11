using System.Net.Http;
using Downloader.Desktop.Plugins;

namespace Downloader.Desktop.Plugins.Website;

/// <summary>
/// Fallback resolver whose only job is offering the "Offline copy (.zip)" variant for page-like URLs.
/// It never changes normal downloads: the default resolve is a pass-through, and <see cref="IsFallback"/>
/// keeps specific resolvers (GitHub, video sites, …) winning whenever they claim the same link.
/// </summary>
internal sealed class WebsiteResolver : ILinkResolver
{
    /// <summary>Scheme prefix the offline-copy variant substitutes into the item URL. Routing on a
    /// dedicated scheme keeps the choice unambiguous across retries and restarts.</summary>
    public const string Scheme = "websitezip:";

    private static readonly string[] PageExtensions =
        { "", ".html", ".htm", ".php", ".asp", ".aspx", ".jsp", ".cfm", ".shtml" };

    private static readonly HttpClient Http = CreateClient();

    public bool IsFallback => true;

    public bool CanResolve(string url) =>
        IsSchemeUrl(url) || LooksLikePage(url);

    public static bool IsSchemeUrl(string url) =>
        url != null && url.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

    /// <summary>Cheap, sync heuristic: an http(s) URL whose last path segment has no extension or a
    /// typical page extension. The variant offer is confirmed by a real content-type probe later.</summary>
    public static bool LooksLikePage(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) ||
            (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
            return false;
        var last = u.AbsolutePath.TrimEnd('/').Split('/')[^1];
        var dot = last.LastIndexOf('.');
        var ext = dot < 0 ? "" : last[dot..].ToLowerInvariant();
        return Array.IndexOf(PageExtensions, ext) >= 0;
    }

    /// <summary>Pass-through: picking no variant must behave exactly like a plain add. A "websitezip:"
    /// URL never reaches here — the transfer provider claims it before resolution.</summary>
    public Task<DownloadPlan> ResolveAsync(string url, CancellationToken cancellationToken)
    {
        var target = IsSchemeUrl(url) ? url[Scheme.Length..] : url;
        return Task.FromResult(new DownloadPlan
        {
            Parts = new[] { new DownloadPart { Url = target } }
        });
    }

    public async Task<IReadOnlyList<LinkVariant>?> GetVariantsAsync(
        string url, ResolveOptions? options, CancellationToken cancellationToken)
    {
        if (!LooksLikePage(url) || !await ServesHtmlAsync(url, cancellationToken).ConfigureAwait(false))
            return null;

        return new[]
        {
            new LinkVariant
            {
                Id = "offline-zip",
                Label = "Offline copy (.zip)",
                Description = "Save this page (and linked pages on the same site) with styles, images " +
                              "and scripts for offline viewing",
                IsDefault = false, // unchecked — leaving it unchecked keeps the plain file download
                SubstituteUrl = Scheme + url
            }
        };
    }

    /// <summary>Bounded content-type probe: HEAD first, ranged GET when HEAD is rejected. Any failure
    /// means "no variant" — it must never block or delay adding a download.</summary>
    private static async Task<bool> ServesHtmlAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            using var head = new HttpRequestMessage(HttpMethod.Head, url);
            try
            {
                using var resp = await Http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                    .ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                    return IsHtml(resp.Content.Headers.ContentType?.MediaType);
            }
            catch (HttpRequestException)
            {
                // fall through to the ranged GET — some servers reject HEAD outright
            }

            using var get = new HttpRequestMessage(HttpMethod.Get, url);
            get.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            using var getResp = await Http.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);
            return getResp.IsSuccessStatusCode && IsHtml(getResp.Content.Headers.ContentType?.MediaType);
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsHtml(string? mediaType) =>
        mediaType != null &&
        (mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase) ||
         mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase));

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; Downloader)");
        return client;
    }
}
