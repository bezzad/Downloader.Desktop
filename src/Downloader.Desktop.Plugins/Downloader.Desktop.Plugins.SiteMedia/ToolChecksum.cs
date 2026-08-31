using System.Security.Cryptography;

namespace Downloader.Desktop.Plugins.SiteMedia;

/// <summary>
/// Verifying a downloaded tool before it is ever executed. The tool is fetched from its publisher at
/// runtime rather than bundled, so "what arrived is what the publisher published" is the only thing
/// standing between a hijacked download and code running on the user's machine — a mismatch must discard
/// the file and never reach <c>Process.Start</c>.
/// <para>
/// Both publishers ship the digests next to the asset: yt-dlp as one <c>SHA2-256SUMS</c> listing every
/// asset, Deno as a per-asset <c>.sha256sum</c> file. Most are the coreutils shape
/// <c>&lt;hex&gt;  &lt;name&gt;</c> — but Deno's WINDOWS assets are published as PowerShell
/// <c>Get-FileHash</c> output instead (<c>Algorithm/Hash/Path</c> lines, uppercase digest). Reading only
/// the coreutils shape meant every Windows install of this plugin discarded a perfectly good Deno archive
/// and reported that the published checksum could not be read (issue #11), so both shapes are parsed.
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

        var entries = ParseCoreutils(sumsFileContent);
        if (entries.Count == 0)
            entries = ParseGetFileHash(sumsFileContent);

        if (entries.Count == 0) return null;
        var match = entries.FirstOrDefault(e =>
            string.Equals(e.Name, assetName, StringComparison.OrdinalIgnoreCase));
        if (match.Hash is not null) return match.Hash;
        return allowSingleEntry && entries.Count == 1 ? entries[0].Hash : null;
    }

    /// <summary>The coreutils shape: <c>&lt;hex&gt;  &lt;name&gt;</c>, one asset per line.</summary>
    private static List<(string Hash, string Name)> ParseCoreutils(string content)
    {
        var entries = new List<(string Hash, string Name)>();
        foreach (var raw in content.Split('\n'))
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
        return entries;
    }

    /// <summary>
    /// PowerShell <c>Get-FileHash</c> output — what Deno publishes for its Windows assets:
    /// <code>
    /// Algorithm : SHA256
    /// Hash      : 15E5300B0BA3C3695A7621D90160A746EC9E710228CEE639AFA9D580F6E3CD11
    /// Path      : C:\a\deno\deno\target\release\deno-x86_64-pc-windows-msvc.zip
    /// </code>
    /// One record per blank-line-separated block. The Path is the publisher's own build machine, so only
    /// its file name is of any use here — and it is what lets the digest still be matched BY NAME rather
    /// than trusted because it was the only one in the file.
    /// </summary>
    private static List<(string Hash, string Name)> ParseGetFileHash(string content)
    {
        var entries = new List<(string Hash, string Name)>();
        string? hash = null;
        var name = string.Empty;

        void Flush()
        {
            if (hash is not null)
                entries.Add((hash.ToLowerInvariant(), name));
            hash = null;
            name = string.Empty;
        }

        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                Flush();
                continue;
            }
            // Split on the FIRST colon only: a Windows path in the value has one of its own ("C:\...").
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            if (key.Equals("Hash", StringComparison.OrdinalIgnoreCase) && IsHex64(value))
                hash = value;
            else if (key.Equals("Path", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                name = Path.GetFileName(value.Replace('\\', '/'));
        }
        Flush();
        return entries;
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
