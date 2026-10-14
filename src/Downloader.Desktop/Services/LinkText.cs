using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Downloader.Desktop.Services;

/// <summary>
/// Turns pasted text into download links. One rule for every way a link arrives (the Add dialog, the
/// top paste box, the clipboard suggestion, the CLI and the local API), so a link means the same thing
/// whichever way it was handed over. Pure.
/// </summary>
public static partial class LinkText
{
    // New lines always separate links. Within a line, spaces, tabs, commas and semicolons separate them
    // too — a batch pasted into a box that collapsed its new lines, or a "a, b; c" list, still splits.
    private static readonly char[] LineBreaks = { '\n', '\r' };
    private static readonly char[] InLineSeparators = { '\t', ' ', ',', ';' };

    /// <summary>
    /// The links in <paramref name="raw"/>. A line that starts with an http(s) link and whose other
    /// space-separated parts are not links is ONE link whose path contains spaces (issue #19: a service
    /// hands out <c>https://example.com/my file.zip</c>, which used to become two downloads); its
    /// whitespace is percent-encoded. A line holding several links still splits into each of them.
    /// </summary>
    public static IReadOnlyList<string> Split(string raw)
    {
        var links = new List<string>();
        foreach (var line in (raw ?? string.Empty).Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(InLineSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1 && IsHttpLink(parts[0]) && !parts.Skip(1).Any(LooksLikeLink))
                links.Add(EscapeWhitespace(line.Trim(InLineSeparators)));
            else
                links.AddRange(parts);
        }
        return links;
    }

    /// <summary>Percent-encodes the whitespace inside a link (<c>my file.zip</c> → <c>my%20file.zip</c>),
    /// after trimming its ends. Everything else is left exactly as given.</summary>
    public static string EscapeWhitespace(string link)
    {
        var trimmed = link?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !trimmed.Any(char.IsWhiteSpace))
            return trimmed;
        var sb = new StringBuilder(trimmed.Length + 8);
        foreach (var c in trimmed)
            sb.Append(char.IsWhiteSpace(c) ? Uri.EscapeDataString(c.ToString()) : c.ToString());
        return sb.ToString();
    }

    public static bool IsHttpLink(string s) =>
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    // Anything with a scheme counts as a link of its own (an http link, but also a plugin's own form such
    // as a model tag "gemma3:12b"), so a line mixing those keeps splitting the way it always did.
    private static bool LooksLikeLink(string part) => SchemePrefix().IsMatch(part);

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*:\S")]
    private static partial Regex SchemePrefix();
}
