using System.Security.Cryptography;

namespace Downloader.Desktop.Plugins.SiteMedia;

/// <summary>
/// Verifying a downloaded tool before it is ever executed. The tool is fetched from its publisher at
/// runtime rather than bundled, so "what arrived is what the publisher published" is the only thing
/// standing between a hijacked download and code running on the user's machine — a mismatch must discard
/// the file and never reach <c>Process.Start</c>.
/// <para>
/// Both publishers ship the digests next to the asset: yt-dlp as one <c>SHA2-256SUMS</c> listing every
/// asset, Deno as a per-asset <c>.sha256sum</c> file. Both use the coreutils shape
/// <c>&lt;hex&gt;  &lt;name&gt;</c>, so one parser reads either.
/// </para>
/// </summary>
internal static class ToolChecksum
{
    /// <summary>Find the digest published for <paramref name="assetName"/> in a coreutils-style sums file.
    /// Returns null when the asset is not listed — which callers must treat as "refuse to run it", never as
    /// "close enough". Set <paramref name="allowSingleEntry"/> for a per-asset sums file (Deno's form),
    /// which was fetched by that one asset's own URL and whose name column has carried path prefixes; a
    /// multi-asset listing (yt-dlp's) must always match by name.</summary>
    internal static string? ParseSums(string sumsFileContent, string assetName, bool allowSingleEntry = false)
    {
        if (string.IsNullOrWhiteSpace(sumsFileContent)) return null;

        var entries = new List<(string Hash, string Name)>();
        foreach (var raw in sumsFileContent.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            // "<hex>  <name>" — the name may itself be a path ("./yt-dlp_linux") or carry a binary marker.
            var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) continue;
            var hash = parts[0].Trim();
            if (!IsHex64(hash)) continue;
            var name = parts[1].Trim().TrimStart('*');
            entries.Add((hash.ToLowerInvariant(), Path.GetFileName(name)));
        }

        if (entries.Count == 0) return null;
        var match = entries.FirstOrDefault(e =>
            string.Equals(e.Name, assetName, StringComparison.OrdinalIgnoreCase));
        if (match.Hash is not null) return match.Hash;
        return allowSingleEntry && entries.Count == 1 ? entries[0].Hash : null;
    }

    private static bool IsHex64(string s) =>
        s.Length == 64 && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    /// <summary>The sha256 of a file, lowercase hex.</summary>
    internal static async Task<string> Sha256HexAsync(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Throw and DELETE the file unless it matches <paramref name="expectedHex"/>. Deleting is the
    /// point: leaving a rejected download on disk would let a later "is it installed?" check run it.</summary>
    internal static async Task VerifyOrDiscardAsync(
        string path, string expectedHex, string displayName, CancellationToken ct)
    {
        var actual = await Sha256HexAsync(path, ct).ConfigureAwait(false);
        if (string.Equals(actual, expectedHex, StringComparison.OrdinalIgnoreCase)) return;

        ToolFile.DeleteIfPresent(path);
        throw new InvalidOperationException(
            $"The downloaded {displayName} did not match the checksum its publisher lists for it, so it was " +
            "discarded and never run. This is usually a corrupted or intercepted download — check your " +
            "connection and try again.");
    }
}
