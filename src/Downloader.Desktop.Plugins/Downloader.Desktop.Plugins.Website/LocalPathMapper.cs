using System.Security.Cryptography;
using System.Text;

namespace Downloader.Desktop.Plugins.Website;

/// <summary>
/// Pure URL → local layout mapping. Every captured URL gets a deterministic zip-relative path (decided
/// at enqueue time, BEFORE download, so documents processed earlier can already rewrite references to
/// documents captured later): <c>&lt;host&gt;/&lt;path…&gt;</c>, directory URLs become
/// <c>index.html</c>, a query string is hashed into the file name, and pages always end in
/// <c>.html</c> so they render when browsed from disk (wget's --adjust-extension).
/// </summary>
internal static class LocalPathMapper
{
    /// <summary>Zip-relative local path (forward slashes) for a URL. <paramref name="isPage"/> forces a
    /// .html extension.</summary>
    public static string MapToLocalPath(Uri url, bool isPage)
    {
        var segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Sanitize(Uri.UnescapeDataString(s)))
            .Where(s => s.Length > 0)
            .ToList();

        var isDirectory = url.AbsolutePath.EndsWith('/') || segments.Count == 0;
        var fileName = isDirectory ? "index.html" : segments[^1];
        if (!isDirectory)
            segments.RemoveAt(segments.Count - 1);

        if (!string.IsNullOrEmpty(url.Query))
            fileName = InsertQueryHash(fileName, url.Query);

        if (isPage && !fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
            fileName += ".html";

        segments.Insert(0, Sanitize(url.Host));
        segments.Add(fileName);
        return string.Join('/', segments);
    }

    /// <summary>Relative reference from one local file to another (both zip-relative, '/'-separated),
    /// each segment percent-escaped so the result is a valid href when browsing from disk.</summary>
    public static string RelativePath(string fromLocalPath, string toLocalPath)
    {
        var from = fromLocalPath.Split('/');
        var to = toLocalPath.Split('/');

        var common = 0;
        while (common < from.Length - 1 && common < to.Length - 1 &&
               string.Equals(from[common], to[common], StringComparison.Ordinal))
            common++;

        var sb = new StringBuilder();
        for (var i = common; i < from.Length - 1; i++)
            sb.Append("../");
        for (var i = common; i < to.Length; i++)
        {
            if (i > common)
                sb.Append('/');
            sb.Append(Uri.EscapeDataString(to[i]));
        }
        return sb.Length == 0 ? Uri.EscapeDataString(to[^1]) : sb.ToString();
    }

    /// <summary>"page.php" + "?id=3" → "page_q1a2b3c4.php" — distinct query strings stay distinct files.</summary>
    private static string InsertQueryHash(string fileName, string query)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query)))[..8].ToLowerInvariant();
        var dot = fileName.LastIndexOf('.');
        return dot <= 0 ? $"{fileName}_q{hash}" : $"{fileName[..dot]}_q{hash}{fileName[dot..]}";
    }

    private static string Sanitize(string segment)
    {
        var sb = new StringBuilder(segment.Length);
        foreach (var c in segment)
            sb.Append(c is '<' or '>' or ':' or '"' or '\\' or '|' or '?' or '*' || char.IsControl(c) ? '_' : c);
        var s = sb.ToString().Trim().TrimEnd('.');
        // never let a path segment walk out of the workdir
        return s is "" or "." or ".." ? "_" : s;
    }
}
