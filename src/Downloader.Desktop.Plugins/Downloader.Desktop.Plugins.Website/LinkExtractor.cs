using System.Text.RegularExpressions;

namespace Downloader.Desktop.Plugins.Website;

/// <summary>How a reference is used — decides whether it can recurse (pages) or is fetched once (requisites).</summary>
internal enum RefKind
{
    /// <summary>An <c>&lt;a href&gt;</c> / <c>&lt;iframe src&gt;</c> — recursed only same-host within the depth cap.</summary>
    PageLink,
    /// <summary>A CSS file (<c>&lt;link rel="stylesheet"&gt;</c> / <c>@import</c>) — fetched and re-parsed for nested assets.</summary>
    Stylesheet,
    /// <summary>Any other page requisite (script/image/font/media) — fetched once, from any host.</summary>
    Requisite
}

/// <summary>One URL occurrence inside a document: exact position + raw value, so rewriting can splice the
/// local path in without touching anything else.</summary>
internal sealed record UrlRef(int Index, int Length, string Value, RefKind Kind);

/// <summary>
/// Pure, regex-based reference extraction from HTML and CSS (the wget-grade approach — no DOM, no JS).
/// Returns every candidate occurrence with its exact character span; callers decide what to capture and
/// rewrite. Values are returned verbatim (still HTML-entity/whitespace as authored).
/// </summary>
internal static partial class LinkExtractor
{
    [GeneratedRegex("""<a\b[^>]*?\bhref\s*=\s*("(?<v>[^"]*)"|'(?<v>[^']*)'|(?<v>[^\s>]+))""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorHref();

    [GeneratedRegex("""<iframe\b[^>]*?\bsrc\s*=\s*("(?<v>[^"]*)"|'(?<v>[^']*)'|(?<v>[^\s>]+))""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex IframeSrc();

    [GeneratedRegex("""<link\b[^>]*?\bhref\s*=\s*("(?<v>[^"]*)"|'(?<v>[^']*)'|(?<v>[^\s>]+))""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LinkHref();

    [GeneratedRegex("""<(script|img|source|video|audio|embed|track|input)\b[^>]*?\bsrc\s*=\s*("(?<v>[^"]*)"|'(?<v>[^']*)'|(?<v>[^\s>]+))""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MediaSrc();

    [GeneratedRegex("""\bposter\s*=\s*("(?<v>[^"]*)"|'(?<v>[^']*)'|(?<v>[^\s>]+))""",
        RegexOptions.IgnoreCase)]
    private static partial Regex Poster();

    [GeneratedRegex("""\bsrcset\s*=\s*("(?<v>[^"]*)"|'(?<v>[^']*)')""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SrcSet();

    [GeneratedRegex("""url\(\s*(?:"(?<v>[^"]*)"|'(?<v>[^']*)'|(?<v>[^)'"\s]+))\s*\)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex CssUrl();

    [GeneratedRegex("""@import\s+(?:"(?<v>[^"]*)"|'(?<v>[^']*)')""",
        RegexOptions.IgnoreCase)]
    private static partial Regex CssImport();

    [GeneratedRegex("""\brel\s*=\s*("(?<v>[^"]*)"|'(?<v>[^']*)'|(?<v>[^\s>]+))""",
        RegexOptions.IgnoreCase)]
    private static partial Regex RelAttr();

    /// <summary>Every URL occurrence in an HTML document, deduplicated by position (a span claimed by a
    /// more specific pattern first wins). srcset values yield one ref per candidate URL.</summary>
    public static List<UrlRef> ExtractHtmlRefs(string html)
    {
        var refs = new List<UrlRef>();
        var seen = new HashSet<int>();

        void Add(Match m, RefKind kind)
        {
            var g = m.Groups["v"];
            if (!g.Success || g.Length == 0 || !seen.Add(g.Index))
                return;
            refs.Add(new UrlRef(g.Index, g.Length, g.Value, kind));
        }

        foreach (Match m in AnchorHref().Matches(html)) Add(m, RefKind.PageLink);
        foreach (Match m in IframeSrc().Matches(html)) Add(m, RefKind.PageLink);

        // <link href> is a stylesheet only when rel says so; icons/preloads are plain requisites.
        foreach (Match m in LinkHref().Matches(html))
        {
            var rel = RelAttr().Match(m.Value).Groups["v"].Value;
            Add(m, rel.Contains("stylesheet", StringComparison.OrdinalIgnoreCase)
                ? RefKind.Stylesheet
                : RefKind.Requisite);
        }

        foreach (Match m in MediaSrc().Matches(html)) Add(m, RefKind.Requisite);
        foreach (Match m in Poster().Matches(html)) Add(m, RefKind.Requisite);

        // srcset = "url1 1x, url2 2x" — each candidate URL becomes its own positioned ref.
        foreach (Match m in SrcSet().Matches(html))
        {
            var g = m.Groups["v"];
            foreach (var (start, length) in SrcSetUrlSpans(g.Value))
                if (seen.Add(g.Index + start))
                    refs.Add(new UrlRef(g.Index + start, length, g.Value.Substring(start, length), RefKind.Requisite));
        }

        // Inline styles + <style> blocks: CSS url() anywhere in the document.
        foreach (Match m in CssUrl().Matches(html)) Add(m, RefKind.Requisite);

        refs.Sort((a, b) => a.Index.CompareTo(b.Index));
        return refs;
    }

    /// <summary>Every URL occurrence in a stylesheet: <c>url(...)</c> requisites (fonts, images) and
    /// <c>@import "..."</c> nested stylesheets.</summary>
    public static List<UrlRef> ExtractCssRefs(string css)
    {
        var refs = new List<UrlRef>();
        var seen = new HashSet<int>();
        foreach (Match m in CssImport().Matches(css))
        {
            var g = m.Groups["v"];
            if (g.Success && g.Length > 0 && seen.Add(g.Index))
                refs.Add(new UrlRef(g.Index, g.Length, g.Value, RefKind.Stylesheet));
        }
        foreach (Match m in CssUrl().Matches(css))
        {
            var g = m.Groups["v"];
            if (g.Success && g.Length > 0 && seen.Add(g.Index))
                refs.Add(new UrlRef(g.Index, g.Length, g.Value, RefKind.Requisite));
        }
        refs.Sort((a, b) => a.Index.CompareTo(b.Index));
        return refs;
    }

    /// <summary>(start, length) spans of the URL tokens inside a srcset attribute value.</summary>
    internal static List<(int Start, int Length)> SrcSetUrlSpans(string srcset)
    {
        var spans = new List<(int, int)>();
        var i = 0;
        while (i < srcset.Length)
        {
            while (i < srcset.Length && (char.IsWhiteSpace(srcset[i]) || srcset[i] == ','))
                i++;
            var start = i;
            while (i < srcset.Length && !char.IsWhiteSpace(srcset[i]) && srcset[i] != ',')
                i++;
            if (i > start)
                spans.Add((start, i - start));
            // skip the descriptor ("1x", "480w") up to the next comma
            while (i < srcset.Length && srcset[i] != ',')
                i++;
        }
        return spans;
    }

    /// <summary>Resolves a raw reference against its document URL. False for anchors-only, non-web
    /// schemes (mailto:, javascript:, data:, …) and anything unparsable. Fragments are stripped.</summary>
    public static bool TryNormalize(Uri baseUrl, string rawValue, out Uri absolute)
    {
        absolute = null!;
        var value = System.Net.WebUtility.HtmlDecode(rawValue ?? string.Empty).Trim();
        if (value.Length == 0 || value.StartsWith('#'))
            return false;
        if (!Uri.TryCreate(baseUrl, value, out var abs))
            return false;
        if (abs.Scheme != Uri.UriSchemeHttp && abs.Scheme != Uri.UriSchemeHttps)
            return false;
        absolute = string.IsNullOrEmpty(abs.Fragment)
            ? abs
            : new UriBuilder(abs) { Fragment = null }.Uri;
        return true;
    }

    /// <summary>Splices replacement values into a document given the original refs (sorted by index).</summary>
    public static string Rewrite(string text, IReadOnlyList<(UrlRef Ref, string NewValue)> replacements)
    {
        if (replacements.Count == 0)
            return text;
        var sb = new System.Text.StringBuilder(text.Length + 256);
        var pos = 0;
        foreach (var (r, newValue) in replacements.OrderBy(r => r.Ref.Index))
        {
            if (r.Index < pos)
                continue; // overlapping span already rewritten
            sb.Append(text, pos, r.Index - pos);
            sb.Append(newValue);
            pos = r.Index + r.Length;
        }
        sb.Append(text, pos, text.Length - pos);
        return sb.ToString();
    }
}
