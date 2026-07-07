using System.Globalization;

namespace Downloader.Desktop.Plugins.Hls;

/// <summary>
/// A small, focused, line-based M3U8 parser. Supports the subset Downloader needs for VOD HLS:
/// master variant selection, media segment lists, <c>#EXT-X-KEY</c> (AES-128 / NONE), <c>#EXT-X-MAP</c>,
/// and <c>#EXT-X-MEDIA-SEQUENCE</c>. Live/DRM/byte-range playlists are out of scope.
/// </summary>
public sealed class M3u8Parser : IM3u8Parser
{
    public bool IsMaster(string content)
    {
        EnsureValid(content);
        return EnumerateLines(content).Any(l => l.StartsWith("#EXT-X-STREAM-INF", StringComparison.Ordinal));
    }

    public HlsMasterPlaylist ParseMaster(string content, Uri baseUri)
    {
        EnsureValid(content);
        var variants = new List<HlsVariant>();
        string? pendingAttrs = null;

        foreach (var line in EnumerateLines(content))
        {
            if (line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal))
            {
                pendingAttrs = line.Substring("#EXT-X-STREAM-INF:".Length);
            }
            else if (!line.StartsWith("#", StringComparison.Ordinal) && pendingAttrs is not null)
            {
                var attrs = ParseAttributes(pendingAttrs);
                long bandwidth = 0;
                if (attrs.TryGetValue("BANDWIDTH", out var bw))
                    long.TryParse(bw, NumberStyles.Integer, CultureInfo.InvariantCulture, out bandwidth);
                attrs.TryGetValue("RESOLUTION", out var res);
                variants.Add(new HlsVariant(Resolve(baseUri, line), bandwidth, res));
                pendingAttrs = null;
            }
        }

        if (variants.Count == 0)
            throw new FormatException("Master playlist contained no #EXT-X-STREAM-INF variants.");

        return new HlsMasterPlaylist(variants);
    }

    public HlsMediaPlaylist ParseMedia(string content, Uri baseUri)
    {
        EnsureValid(content);

        var segments = new List<HlsSegment>();
        string? initSegmentUri = null;
        HlsKey? currentKey = null;
        double pendingDuration = 0;
        long mediaSequence = 0;
        bool sequenceSeen = false;

        foreach (var line in EnumerateLines(content))
        {
            if (line.StartsWith("#EXT-X-MEDIA-SEQUENCE:", StringComparison.Ordinal))
            {
                var v = line.Substring("#EXT-X-MEDIA-SEQUENCE:".Length).Trim();
                long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out mediaSequence);
                sequenceSeen = true;
            }
            else if (line.StartsWith("#EXT-X-KEY:", StringComparison.Ordinal))
            {
                currentKey = ParseKey(line.Substring("#EXT-X-KEY:".Length), baseUri);
            }
            else if (line.StartsWith("#EXT-X-MAP:", StringComparison.Ordinal))
            {
                var attrs = ParseAttributes(line.Substring("#EXT-X-MAP:".Length));
                if (attrs.TryGetValue("URI", out var mapUri) && !string.IsNullOrEmpty(mapUri))
                    initSegmentUri = Resolve(baseUri, mapUri);
            }
            else if (line.StartsWith("#EXTINF:", StringComparison.Ordinal))
            {
                var spec = line.Substring("#EXTINF:".Length);
                var comma = spec.IndexOf(',');
                var durText = (comma >= 0 ? spec.Substring(0, comma) : spec).Trim();
                double.TryParse(durText, NumberStyles.Float, CultureInfo.InvariantCulture, out pendingDuration);
            }
            else if (!line.StartsWith("#", StringComparison.Ordinal))
            {
                // A media URI line. Implicit IV (when #EXT-X-KEY has no IV) defaults to the segment's
                // media sequence number as a 16-byte big-endian value.
                var key = currentKey;
                if (key is { Iv: null } && key.IsEncrypted)
                    key = key with { Iv = SequenceIv(mediaSequence) };

                segments.Add(new HlsSegment(Resolve(baseUri, line), pendingDuration, key, mediaSequence));
                pendingDuration = 0;
                mediaSequence++;
            }
        }

        if (segments.Count == 0)
            throw new FormatException("Media playlist contained no segments (#EXTINF).");

        // (sequenceSeen kept for clarity/future use; absence is valid and defaults to 0.)
        _ = sequenceSeen;
        return new HlsMediaPlaylist(segments, initSegmentUri);
    }

    private static HlsKey ParseKey(string attrText, Uri baseUri)
    {
        var attrs = ParseAttributes(attrText);
        var method = attrs.TryGetValue("METHOD", out var m) ? m : "NONE";
        var uri = attrs.TryGetValue("URI", out var u) && !string.IsNullOrEmpty(u) ? Resolve(baseUri, u) : string.Empty;
        byte[]? iv = null;
        if (attrs.TryGetValue("IV", out var ivText) && !string.IsNullOrWhiteSpace(ivText))
            iv = ParseHex(ivText);
        return new HlsKey(method, uri, iv);
    }

    /// <summary>Parse comma-separated <c>KEY=VALUE</c> attributes, honoring quoted values that may contain commas.</summary>
    internal static Dictionary<string, string> ParseAttributes(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        while (i < text.Length)
        {
            int eq = text.IndexOf('=', i);
            if (eq < 0) break;
            var key = text.Substring(i, eq - i).Trim();
            int valStart = eq + 1;
            string value;
            if (valStart < text.Length && text[valStart] == '"')
            {
                int end = text.IndexOf('"', valStart + 1);
                if (end < 0) end = text.Length;
                value = text.Substring(valStart + 1, end - valStart - 1);
                i = end + 1;
                while (i < text.Length && text[i] != ',') i++;
                i++; // skip comma
            }
            else
            {
                int comma = text.IndexOf(',', valStart);
                if (comma < 0) comma = text.Length;
                value = text.Substring(valStart, comma - valStart).Trim();
                i = comma + 1;
            }
            if (key.Length > 0) result[key] = value;
        }
        return result;
    }

    internal static byte[] ParseHex(string hex)
    {
        hex = hex.Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || hex.StartsWith("0X", StringComparison.Ordinal))
            hex = hex.Substring(2);
        if (hex.Length % 2 != 0)
            throw new FormatException($"Invalid hex value: '{hex}'.");
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    internal static byte[] SequenceIv(long sequence)
    {
        var iv = new byte[16];
        var be = BitConverter.GetBytes(sequence);
        if (BitConverter.IsLittleEndian) Array.Reverse(be);
        // place the 8-byte sequence at the end (big-endian) of the 16-byte IV
        Array.Copy(be, 0, iv, 16 - be.Length, be.Length);
        return iv;
    }

    private static string Resolve(Uri baseUri, string uri)
    {
        // new Uri(base, reference) handles relative, root-relative ("/seg.m4s") and absolute references
        // per RFC 3986. Do NOT pre-check with Uri.TryCreate(Absolute): on Unix a root-relative path parses
        // as an absolute file:/// URI, which turned x.com playlist segments into file:// URLs.
        return new Uri(baseUri, uri.Trim()).ToString();
    }

    private static IEnumerable<string> EnumerateLines(string content)
    {
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length > 0) yield return line;
        }
    }

    private static void EnsureValid(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new FormatException("Playlist is empty.");
        var first = EnumerateLines(content).FirstOrDefault();
        if (first is null || !first.StartsWith("#EXTM3U", StringComparison.Ordinal))
            throw new FormatException("Not a valid M3U8 playlist (missing #EXTM3U header).");
    }
}
