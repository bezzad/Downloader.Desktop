using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Downloader.Desktop.Plugins.Hls.Dash;

/// <summary>
/// Turns an MPD document into <see cref="DashManifest"/>. Matching is done on element/attribute
/// <em>local</em> names: manifests in the wild use several namespace URIs for the DASH schema and some omit
/// the namespace entirely, so binding to one URI would reject perfectly good files.
/// </summary>
public sealed class MpdParser : IMpdParser
{
    /// <summary>A template placeholder: <c>$$</c>, <c>$Number$</c>, <c>$Time%09d$</c>, …</summary>
    private static readonly Regex Placeholder =
        new(@"\$(?<id>[A-Za-z]*)(?<fmt>%0\d+[dxXou])?\$", RegexOptions.Compiled);

    public DashManifest Parse(string content, Uri baseUri)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(content);
        }
        catch (XmlException ex)
        {
            throw new DashException("This does not look like a DASH manifest: " + ex.Message);
        }

        var mpd = doc.Root;
        if (mpd is null || !Is(mpd, "MPD"))
            throw new DashException("This does not look like a DASH manifest (no <MPD> root element).");

        if (string.Equals(Attr(mpd, "type"), "dynamic", StringComparison.OrdinalIgnoreCase))
            throw new DashException(
                "This is a live DASH stream. Live streams have no fixed end and cannot be downloaded as a file.");

        if (mpd.Descendants().Any(e => Is(e, "ContentProtection")))
            throw new DashException(
                "This DASH stream is protected by DRM and cannot be downloaded.");

        var period = Children(mpd, "Period").FirstOrDefault()
                     ?? throw new DashException("This DASH manifest contains no period to download.");

        var duration = DurationSeconds(mpd, period);

        var mpdBase = ResolveBase(baseUri, mpd);
        var periodBase = ResolveBase(mpdBase, period);

        var representations = new List<DashRepresentation>();
        foreach (var set in Children(period, "AdaptationSet"))
        {
            var setBase = ResolveBase(periodBase, set);
            foreach (var rep in Children(set, "Representation"))
                representations.Add(BuildRepresentation(rep, set, setBase, duration));
        }

        if (representations.Count == 0)
            throw new DashException("This DASH manifest contains no media representations.");

        return new DashManifest(duration, representations);
    }

    // ── representation ──────────────────────────────────────────────────────────────────────────────

    private static DashRepresentation BuildRepresentation(
        XElement rep, XElement set, Uri setBase, double periodDuration)
    {
        var repBase = ResolveBase(setBase, rep);
        var id = Attr(rep, "id") ?? "r" + Math.Abs(rep.GetHashCode()).ToString(CultureInfo.InvariantCulture);
        var bandwidth = Long(rep, "bandwidth") ?? 0;
        var codecs = Attr(rep, "codecs") ?? Attr(set, "codecs");
        var kind = KindOf(set, rep, codecs);
        var language = Attr(set, "lang") ?? Attr(rep, "lang");

        // Addressing may be declared on the representation or inherited from the adaptation set; the
        // representation's own declaration wins.
        var template = Child(rep, "SegmentTemplate") ?? Child(set, "SegmentTemplate");
        var list = Child(rep, "SegmentList") ?? Child(set, "SegmentList");

        string? init = null;
        IReadOnlyList<string> segments;
        var singleFile = false;

        if (template is not null)
        {
            (init, segments) = FromTemplate(template, repBase, id, bandwidth, periodDuration);
        }
        else if (list is not null)
        {
            (init, segments) = FromList(list, repBase);
        }
        else
        {
            // SegmentBase or a bare BaseURL: the representation IS one complete file. We want all of it, so
            // there is nothing to expand and no byte ranges to request — the engine chunks it normally.
            singleFile = true;
            segments = new[] { repBase.ToString() };
        }

        if (segments.Count == 0)
            throw new DashException($"Representation '{id}' in this DASH manifest has no segments.");

        return new DashRepresentation(
            id, kind, bandwidth,
            Int(rep, "width") ?? Int(set, "width") ?? 0,
            Int(rep, "height") ?? Int(set, "height") ?? 0,
            codecs, language, init, segments, singleFile);
    }

    private static DashStreamKind KindOf(XElement set, XElement rep, string? codecs)
    {
        var declared = Attr(set, "contentType") ?? Attr(rep, "contentType")
                       ?? Attr(set, "mimeType") ?? Attr(rep, "mimeType");
        if (!string.IsNullOrEmpty(declared))
        {
            if (declared.StartsWith("video", StringComparison.OrdinalIgnoreCase)) return DashStreamKind.Video;
            if (declared.StartsWith("audio", StringComparison.OrdinalIgnoreCase)) return DashStreamKind.Audio;
            if (declared.StartsWith("text", StringComparison.OrdinalIgnoreCase)
                || declared.StartsWith("application", StringComparison.OrdinalIgnoreCase))
                return DashStreamKind.Other;
        }

        // No usable mimeType/contentType (it happens): fall back to the codec family.
        var c = codecs?.Split(',')[0].Trim().ToLowerInvariant() ?? string.Empty;
        if (c.StartsWith("avc") || c.StartsWith("hev") || c.StartsWith("hvc")
            || c.StartsWith("vp8") || c.StartsWith("vp9") || c.StartsWith("av01"))
            return DashStreamKind.Video;
        if (c.StartsWith("mp4a") || c.StartsWith("opus") || c.StartsWith("ac-3")
            || c.StartsWith("ec-3") || c.StartsWith("vorbis") || c.StartsWith("flac"))
            return DashStreamKind.Audio;

        return DashStreamKind.Other;
    }

    // ── addressing modes ────────────────────────────────────────────────────────────────────────────

    private static (string? Init, IReadOnlyList<string> Segments) FromTemplate(
        XElement template, Uri repBase, string repId, long bandwidth, double periodDuration)
    {
        var media = Attr(template, "media")
                    ?? throw new DashException("A DASH SegmentTemplate is missing its 'media' attribute.");
        var initTemplate = Attr(template, "initialization") ?? Attr(template, "initialisation");
        var timescale = Long(template, "timescale") ?? 1;
        if (timescale <= 0) timescale = 1;
        var startNumber = Long(template, "startNumber") ?? 1;

        string? init = initTemplate is null
            ? null
            : Abs(repBase, Substitute(initTemplate, repId, bandwidth, number: null, time: null));

        var timeline = Child(template, "SegmentTimeline");
        var segments = new List<string>();

        if (timeline is not null)
        {
            long number = startNumber;
            long time = 0;
            var total = (long)Math.Round(periodDuration * timescale);

            foreach (var s in Children(timeline, "S"))
            {
                if (Long(s, "t") is { } t) time = t;
                var d = Long(s, "d")
                        ?? throw new DashException("A DASH SegmentTimeline entry is missing its duration.");
                if (d <= 0)
                    throw new DashException("A DASH SegmentTimeline entry has a non-positive duration.");

                var repeat = Long(s, "r") ?? 0;
                if (repeat < 0)
                {
                    // r="-1" means "repeat until the end of the period" — derive the count from the
                    // declared duration, which is the only bound a static manifest gives us.
                    repeat = total > time ? Math.Max(0, (long)Math.Ceiling((total - time) / (double)d) - 1) : 0;
                }

                for (long i = 0; i <= repeat; i++)
                {
                    segments.Add(Abs(repBase, Substitute(media, repId, bandwidth, number, time)));
                    time += d;
                    number++;
                }
            }
        }
        else
        {
            var segDuration = Long(template, "duration")
                              ?? throw new DashException(
                                  "A DASH SegmentTemplate has neither a SegmentTimeline nor a segment duration.");
            if (segDuration <= 0)
                throw new DashException("A DASH SegmentTemplate has a non-positive segment duration.");
            if (periodDuration <= 0)
                throw new DashException(
                    "This DASH manifest declares no duration, so its segment count cannot be determined.");

            var seconds = segDuration / (double)timescale;
            var count = (long)Math.Ceiling(periodDuration / seconds - 1e-6);
            for (long i = 0; i < count; i++)
                segments.Add(Abs(repBase,
                    Substitute(media, repId, bandwidth, startNumber + i, (long)(i * segDuration))));
        }

        return (init, segments);
    }

    private static (string? Init, IReadOnlyList<string> Segments) FromList(XElement list, Uri repBase)
    {
        string? init = null;
        var initElement = Child(list, "Initialization") ?? Child(list, "Initialisation");
        if (initElement is not null && Attr(initElement, "sourceURL") is { } src)
            init = Abs(repBase, src);

        var segments = Children(list, "SegmentURL")
            .Select(s => Attr(s, "media"))
            .Where(m => !string.IsNullOrEmpty(m))
            .Select(m => Abs(repBase, m!))
            .ToList();

        return (init, segments);
    }

    /// <summary>
    /// Substitute the identifiers of a segment template. <c>$$</c> is a literal <c>$</c>; the optional
    /// <c>%0Nd</c> form (e.g. <c>$Number%05d$</c>) zero-pads and is common enough that ignoring it would
    /// produce wrong URLs. An unknown identifier is left as-is rather than dropped, so a broken URL is
    /// visible instead of silently mangled.
    /// </summary>
    internal static string Substitute(string template, string repId, long bandwidth, long? number, long? time)
        => Placeholder.Replace(template, m =>
        {
            var id = m.Groups["id"].Value;
            if (id.Length == 0) return "$";

            string? value = id switch
            {
                "RepresentationID" => repId,
                "Bandwidth" => bandwidth.ToString(CultureInfo.InvariantCulture),
                "Number" => number?.ToString(CultureInfo.InvariantCulture),
                "Time" => time?.ToString(CultureInfo.InvariantCulture),
                _ => null,
            };
            if (value is null) return m.Value;

            var fmt = m.Groups["fmt"].Value;
            if (fmt.Length > 0 && long.TryParse(value, out var numeric))
            {
                // "%0" <width> <conversion>
                var width = int.Parse(fmt[2..^1], CultureInfo.InvariantCulture);
                var conversion = fmt[^1];
                var text = conversion switch
                {
                    'x' => numeric.ToString("x", CultureInfo.InvariantCulture),
                    'X' => numeric.ToString("X", CultureInfo.InvariantCulture),
                    _ => numeric.ToString(CultureInfo.InvariantCulture),
                };
                return text.PadLeft(width, '0');
            }
            return value;
        });

    // ── xml helpers (local-name based, namespace agnostic) ──────────────────────────────────────────

    private static bool Is(XElement e, string name) =>
        string.Equals(e.Name.LocalName, name, StringComparison.Ordinal);

    private static IEnumerable<XElement> Children(XElement parent, string name) =>
        parent.Elements().Where(e => Is(e, name));

    private static XElement? Child(XElement parent, string name) => Children(parent, name).FirstOrDefault();

    private static string? Attr(XElement e, string name) =>
        e.Attributes().FirstOrDefault(a =>
            string.Equals(a.Name.LocalName, name, StringComparison.Ordinal))?.Value;

    private static long? Long(XElement e, string name) =>
        long.TryParse(Attr(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static int? Int(XElement e, string name) =>
        int.TryParse(Attr(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>Compose this element's <c>BaseURL</c> (if any) onto the inherited base.</summary>
    private static Uri ResolveBase(Uri parent, XElement e)
    {
        var value = Child(e, "BaseURL")?.Value?.Trim();
        if (string.IsNullOrEmpty(value)) return parent;
        return Uri.TryCreate(parent, value, out var combined) ? combined : parent;
    }

    private static string Abs(Uri baseUri, string relative) =>
        Uri.TryCreate(baseUri, relative, out var u) ? u.ToString() : relative;

    /// <summary>
    /// The presentation duration in seconds: the MPD's own <c>mediaPresentationDuration</c>, falling back to
    /// the period's <c>duration</c>. Zero when neither is declared (the caller decides whether it can cope).
    /// </summary>
    private static double DurationSeconds(XElement mpd, XElement period)
    {
        foreach (var raw in new[] { Attr(mpd, "mediaPresentationDuration"), Attr(period, "duration") })
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            try
            {
                var span = XmlConvert.ToTimeSpan(raw);
                if (span > TimeSpan.Zero) return span.TotalSeconds;
            }
            catch (FormatException)
            {
                // Not a valid ISO-8601 duration — try the next candidate.
            }
        }
        return 0;
    }

    /// <summary>Debug aid: a compact one-line description of what was parsed.</summary>
    internal static string Describe(DashManifest manifest)
    {
        var sb = new StringBuilder();
        sb.Append(manifest.DurationSeconds.ToString("0.##", CultureInfo.InvariantCulture)).Append("s: ");
        sb.AppendJoin(", ", manifest.Representations.Select(r =>
            $"{r.Id}/{r.Kind}/{r.Bandwidth}bps/{r.SegmentUris.Count}seg"));
        return sb.ToString();
    }
}
