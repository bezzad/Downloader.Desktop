using System;
using System.Collections.Generic;
using Downloader.Desktop.Services;

namespace Downloader.Desktop.Models;

/// <summary>
/// The cookies, headers and referer a single download needs in order to be fetched — handed over by an
/// external tool (typically the browser extension) with the link itself, for sites where the bytes are only
/// served to the session that found them.
/// <para>
/// Cookies and headers are <b>secrets</b>: they live in memory for this session only. They are never written
/// to <c>config.json</c> (the owning <see cref="DownloadItem.Request"/> is <c>[JsonIgnore]</c>) and never
/// logged. The referer is not a credential, so it is persisted separately on the item.
/// </para>
/// </summary>
public sealed class RequestContext
{
    /// <summary>Live-session cookies for this download's URL, as the extension's <c>chrome.cookies.getAll</c>
    /// returns them. Kept as a list (not just the temp file) so the bytes-fetching requests can send them and
    /// a retry in the same session isn't silently anonymous.</summary>
    public List<CookieDto> Cookies { get; set; } = new();

    /// <summary>Extra request headers for this download only. Overrides the global settings on a clash.</summary>
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The page this link was found on. Not a credential, so it persists with the item.</summary>
    public string Referer { get; set; }

    public bool IsEmpty =>
        (Cookies == null || Cookies.Count == 0) &&
        (Headers == null || Headers.Count == 0) &&
        string.IsNullOrWhiteSpace(Referer);
}
