namespace Downloader.Desktop.Plugins.SiteMedia;

/// <summary>What a one-byte probe of an extracted stream URL says about it.</summary>
internal enum ProbeVerdict
{
    /// <summary>The server served the byte — the host can download this URL.</summary>
    Ok,

    /// <summary>The server refused it outright (401/403/410). A different extraction may do better;
    /// downloading this URL cannot.</summary>
    Refused,

    /// <summary>Nothing conclusive (network error, timeout, some other status). Never used to reject a
    /// URL: a probe that could not reach the server says nothing about the link.</summary>
    Unknown,
}

/// <summary>Asks whether an extracted stream URL is actually fetchable, before a whole download is
/// planned around it. Behind an interface so the resolver's fallback policy is unit-tested with no
/// network.</summary>
internal interface IMediaProbe
{
    Task<ProbeVerdict> CheckAsync(string url, IReadOnlyDictionary<string, string>? headers, CancellationToken ct);
}

/// <summary>
/// The real probe: one <c>GET</c> for the first byte, carrying the same headers the download will use.
/// It costs one tiny request and catches the case this exists for — YouTube handing back formats from a
/// player client whose URLs its CDN then answers with 403, which used to surface as a raw
/// "403 (Forbidden)" on the row seconds after a successful-looking extraction.
/// </summary>
internal sealed class HttpMediaProbe : IMediaProbe
{
    private readonly HttpClient _http;

    public HttpMediaProbe(HttpClient? http = null)
        => _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<ProbeVerdict> CheckAsync(
        string url, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            return ProbeVerdict.Unknown;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // A range this small is what the download engine's own size probe asks for, so a server that
            // answers it will answer the download too.
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            if (headers != null)
                foreach (var (key, value) in headers)
                    request.Headers.TryAddWithoutValidation(key, value);

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
                return ProbeVerdict.Ok;

            return (int)response.StatusCode is 401 or 403 or 410
                ? ProbeVerdict.Refused
                : ProbeVerdict.Unknown;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return ProbeVerdict.Unknown;
        }
    }
}
