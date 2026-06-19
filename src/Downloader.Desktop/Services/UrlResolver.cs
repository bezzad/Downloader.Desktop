using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Downloader;

namespace Downloader.Desktop.Services;

/// <summary>
/// Thin wrapper over the Downloader engine's <see cref="RemoteFileResolver"/> (exposed in
/// Downloader 5.9.0), which performs the <b>same</b> name/size probe the download pipeline uses
/// internally — a single lightweight <c>Range: 0-0</c> GET that follows redirects — so we can
/// preview a file's name and size <b>without starting a download</b>, and resolve its final
/// (post-redirect) URL for the engine.
///
/// <para>
/// Replacing the previous hand-rolled <see cref="System.Net.Http.HttpClient"/> probe with the
/// engine's resolver means previews now honor the user's request settings (headers, proxy,
/// credentials, cookies, redirect policy) and a single probe yields the name, size and final
/// address together (#4).
/// </para>
///
/// <para>
/// The resolver doc notes callers that probe many URLs should add their own concurrency limiting
/// and timeouts; we keep the previous tuning so adding many URLs (or a slow/unresponsive server)
/// can't leave rows stuck on "Fetching name…" or starve the UI (#10).
/// </para>
/// </summary>
public static class UrlResolver
{
    // Cap how many preview probes run at once and time-box each one (background, best-effort).
    private static readonly SemaphoreSlim PreviewGate = new(3, 3);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Follows redirects (best-effort) and returns the final URL so the engine receives a direct
    /// link. Used at download start; the engine also follows redirects, so this is an optimization
    /// only. Returns the original URL on any failure. Not gated: starting a download must not wait
    /// behind background preview probes.
    /// </summary>
    public static async Task<string> ResolveAsync(string url, DownloadConfiguration configuration = null)
    {
        if (!IsHttp(url, out _))
            return url;

        var info = await ProbeAsync(url, configuration, gated: false).ConfigureAwait(false);
        return info?.Address?.AbsoluteUri ?? url;
    }

    /// <summary>
    /// The file name embedded in the URL path, if it already looks like a real file (has an
    /// extension). Pure/synchronous and free — lets the UI show a name instantly before any probe.
    /// Returns null when the URL carries no usable name.
    /// </summary>
    public static string NameFromUrl(string url)
    {
        if (!IsHttp(url, out var uri))
            return null;
        var name = SanitizeFileName(Uri.UnescapeDataString(Path.GetFileName(uri.LocalPath)));
        return !string.IsNullOrWhiteSpace(name) && Path.HasExtension(name) ? name : null;
    }

    /// <summary>
    /// Best-effort file-name resolution without downloading: uses the cheap URL fast-path first
    /// (no network) and only probes the server when the URL carries no usable name. Returns null
    /// when nothing could be resolved.
    /// </summary>
    public static async Task<string> ResolveFileNameAsync(string url, DownloadConfiguration configuration = null)
    {
        var fromUrl = NameFromUrl(url);
        if (!string.IsNullOrWhiteSpace(fromUrl))
            return fromUrl;

        var info = await ProbeAsync(url, configuration, gated: true).ConfigureAwait(false);
        return SanitizeFileName(info?.FileName) ?? UrlPathName(url);
    }

    /// <summary>
    /// Best-effort name + size + range-support probe without downloading, gated and time-boxed.
    /// Returns null when the probe could not run (too busy, timed out, failed, or non-http URL).
    /// </summary>
    public static Task<RemoteFileInfo> ResolveFileInfoAsync(string url, DownloadConfiguration configuration = null)
        => ProbeAsync(url, configuration, gated: true);

    /// <summary>
    /// Runs <see cref="RemoteFileResolver.GetFileInfoAsync(string, DownloadConfiguration, CancellationToken)"/>
    /// with an optional concurrency gate and a hard timeout. Never throws — returns null on any failure.
    /// </summary>
    private static async Task<RemoteFileInfo> ProbeAsync(string url, DownloadConfiguration configuration, bool gated)
    {
        if (!IsHttp(url, out _))
            return null;

        if (gated && !await PreviewGate.WaitAsync(ProbeTimeout).ConfigureAwait(false))
            return null; // too busy — caller falls back to a URL-derived name

        try
        {
            using var cts = new CancellationTokenSource(ProbeTimeout);
            return await RemoteFileResolver
                .GetFileInfoAsync(url, configuration, cts.Token)
                .ConfigureAwait(false);
        }
        catch
        {
            return null; // network/timeout/cancel/server hiding info — best effort
        }
        finally
        {
            if (gated)
                PreviewGate.Release();
        }
    }

    private static string UrlPathName(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? SanitizeFileName(Uri.UnescapeDataString(Path.GetFileName(uri.LocalPath)))
            : null;

    private static bool IsHttp(string url, out Uri uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out uri))
            return false;
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        name = name.Trim('"', ' ');
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
