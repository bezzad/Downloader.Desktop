using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Downloader.Desktop.Services;

/// <summary>
/// Suggests a queue name for a batch of links from the part their file names share — e.g.
/// <c>The.X.Movie.S01.E02.mkv</c> + <c>The.X.Movie.S01.E03.mkv</c> → <c>The.X.Movie</c>.
/// Pure (no I/O): the links are read as text only. Returns null when the links carry no file
/// names to compare, so the caller falls back to a plain "New queue".
/// </summary>
public static class QueueNameSuggester
{
    private static readonly char[] Separators = { '.', '-', '_', ' ', '+' };

    // A trailing token that only numbers the batch (S01, E02, part3, 2024) says nothing about what
    // the group IS, so it is dropped from the suggested name.
    private static readonly Regex Counter =
        new("^(?:s|e|ep|season|episode|part|pt|cd|disc|vol|v)?\\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Suggest(IEnumerable<string> urls)
    {
        var tokenLists = (urls ?? Enumerable.Empty<string>())
            .Select(FileNameOf)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(Tokenize)
            .Where(t => t.Count > 0)
            .ToList();

        if (tokenLists.Count < 2)
            return null; // one link (or none with a file name) isn't a batch to name

        var shared = new List<string>();
        for (var i = 0; i < tokenLists[0].Count; i++)
        {
            var token = tokenLists[0][i];
            if (tokenLists.Any(t => i >= t.Count || !string.Equals(t[i], token, StringComparison.OrdinalIgnoreCase)))
                break;
            shared.Add(token);
        }

        while (shared.Count > 0 && Counter.IsMatch(shared[^1]))
            shared.RemoveAt(shared.Count - 1);

        if (shared.Count == 0)
            return null;

        var name = string.Join(".", shared);
        return name.Length < 2 || !name.Any(char.IsLetterOrDigit) ? null : name;
    }

    /// <summary>The file name (without extension) a link points at, or null when it names no file.</summary>
    private static string FileNameOf(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var path = url.Trim();
        var cut = path.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0)
            path = path[..cut];
        path = path.TrimEnd('/');

        var slash = path.LastIndexOf('/');
        var last = slash >= 0 ? path[(slash + 1)..] : path;
        try
        {
            last = Uri.UnescapeDataString(last);
        }
        catch (UriFormatException)
        {
            // a stray '%' is not worth failing over — group on the raw segment
        }

        // No extension ⇒ not a file name (a site/page link), so there is nothing to group on.
        return Path.HasExtension(last) ? Path.GetFileNameWithoutExtension(last) : null;
    }

    private static List<string> Tokenize(string name) =>
        name.Split(Separators, StringSplitOptions.RemoveEmptyEntries).ToList();
}
