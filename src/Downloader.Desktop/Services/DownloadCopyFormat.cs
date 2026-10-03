using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Downloader.Desktop.Models;

namespace Downloader.Desktop.Services;

/// <summary>
/// The text the row menu's Copy submenu puts on the clipboard. Pure, so every format is unit-tested.
/// None of the formats carry cookies, request headers, the cookie file or the plan: those are secrets
/// or internals. The referer is included because it is already saved with the download.
/// </summary>
public static class DownloadCopyFormat
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>One primary URL per line.</summary>
    public static string Links(IEnumerable<DownloadItem> items) =>
        string.Join("\n", items.Select(i => i.Url).Where(u => !string.IsNullOrEmpty(u)));

    /// <summary>An array of { url, mirrors, fileName, folder, size }.</summary>
    public static string Json(IEnumerable<DownloadItem> items) =>
        JsonSerializer.Serialize(items.Select(i => new
        {
            url = i.Url,
            mirrors = i.Urls?.Skip(1).ToList() ?? new List<string>(),
            fileName = i.FileName,
            folder = i.SaveFolder,
            size = i.Size
        }), JsonOptions);

    /// <summary>One <c>curl</c> command per line, POSIX-quoted.</summary>
    public static string Curl(IEnumerable<DownloadItem> items) =>
        string.Join("\n", items.Where(i => !string.IsNullOrEmpty(i.Url)).Select(CurlLine));

    private static string CurlLine(DownloadItem item)
    {
        var line = "curl -L";
        if (!string.IsNullOrWhiteSpace(item.FileName))
            line += " -o " + Quote(item.FileName);
        if (!string.IsNullOrWhiteSpace(item.Referer))
            line += " -e " + Quote(item.Referer);
        return line + " " + Quote(item.Url);
    }

    /// <summary>POSIX single-quote: <c>'</c> becomes <c>'\''</c>.</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
