using System;
using System.Text.RegularExpressions;

namespace Downloader.Desktop.Services;

/// <summary>Makes values safe to write to the log.</summary>
public static class LogText
{
    /// <summary>
    /// A URL as <c>scheme://host[:port]/path</c> only — the query, fragment and any user:password part
    /// are removed, so a signed link's token never reaches the log. Unparsable → <c>&lt;url&gt;</c>.
    /// </summary>
    public static string Url(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return "<url>";
        if (string.IsNullOrEmpty(uri.Host))
            return $"{uri.Scheme}:{StripQuery(uri.AbsolutePath)}";
        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
        return $"{uri.Scheme}://{uri.Host}{port}{uri.AbsolutePath}";
    }

    // A nested scheme (websitezip:https://…) keeps the inner link in its path.
    private static string StripQuery(string path)
    {
        var cut = path.IndexOfAny(['?', '#']);
        return cut < 0 ? path : path[..cut];
    }

    /// <summary>Hides the user:password part of an address (<c>http://***@proxy:8080</c>).</summary>
    public static string MaskUserInfo(string address) =>
        string.IsNullOrEmpty(address) ? address : Regex.Replace(address, "//[^/@]*@", "//***@");
}
