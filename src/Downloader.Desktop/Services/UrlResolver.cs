using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Downloader.Desktop.Services;

/// <summary>
/// Follows HTTP redirects (incl. 301/302/303/307/308) once and returns the final URL, so the
/// download engine receives a direct link. Best-effort: returns the original URL on any failure.
/// </summary>
public static class UrlResolver
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 20 };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }

    public static async Task<string> ResolveAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return url;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            // Range 0-0 keeps it cheap; ResponseHeadersRead avoids downloading the body.
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);

            using var response = await Client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);

            var finalUri = response.RequestMessage?.RequestUri;
            return finalUri?.AbsoluteUri ?? url;
        }
        catch
        {
            return url; // network/timeout/HEAD-unsupported — fall back to the original URL
        }
    }
}
