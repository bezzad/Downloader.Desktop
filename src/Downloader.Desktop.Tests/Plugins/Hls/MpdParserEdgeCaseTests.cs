using System;
using Downloader.Desktop.Plugins.Hls.Dash;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins.Hls;

/// <summary>
/// The MPD parser against manifests that are malformed, unusual or simply not what the happy path
/// assumes. The committed fixtures in <see cref="MpdParserTests"/> cover the shapes the spec
/// describes; this covers what real servers actually emit.
///
/// A parser like this fails in one of two bad ways: it throws on a manifest a player would happily
/// accept (the download never starts, with a stack trace as the explanation), or it silently produces
/// wrong segment URLs (a "downloaded" file that will not play). Both matter more than the happy path,
/// which is why the odd shapes get their own tests: absent attributes, zero and negative durations,
/// the British "Initialisation" spelling, unparsable base URLs, and content that is not a manifest at
/// all.
///
/// Manifests are inline rather than fixtures so each malformation is visible next to its assertion.
/// </summary>
public class MpdParserEdgeCaseTests
{
    private static readonly Uri ManifestUri = new("https://host.example/path/manifest.mpd");

    private static DashManifest Parse(string xml) => new MpdParser().Parse(xml, ManifestUri);

    private static string Wrap(string body, string extra = "") => $"""
        <?xml version="1.0"?>
        <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT10S" {extra}>
          <Period>{body}</Period>
        </MPD>
        """;

    // ---- refusing what we cannot download ---------------------------------

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not xml at all")]
    [InlineData("<html><body>404</body></html>")]
    [InlineData("<NotAnMpd><Period/></NotAnMpd>")]
    [InlineData("<?xml version=\"1.0\"?><Rss><channel/></Rss>")]
    public void Content_that_is_not_a_manifest_is_refused_with_a_reason(string content)
    {
        // A server that answers a .mpd URL with an error page must produce a readable failure, not a
        // parser crash.
        var ex = Assert.Throws<DashException>(() => Parse(content));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_manifest_with_no_period_is_refused()
    {
        Assert.Throws<DashException>(() => Parse("""
            <?xml version="1.0"?>
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT10S" />
            """));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_manifest_with_no_representations_is_refused()
    {
        Assert.Throws<DashException>(() => Parse(Wrap("<AdaptationSet mimeType=\"video/mp4\" />")));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_representation_with_no_segments_is_refused()
    {
        // A representation that resolves to nothing downloadable is worse than a refusal: it would
        // "succeed" and write an empty file.
        Assert.Throws<DashException>(() => Parse(Wrap("""
            <AdaptationSet mimeType="video/mp4">
              <Representation id="v" bandwidth="1000">
                <SegmentTemplate media="$Number$.m4s" timescale="1000" startNumber="1" />
              </Representation>
            </AdaptationSet>
            """)));
    }

    // ---- attributes that are simply absent --------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_representation_without_an_id_still_gets_one()
    {
        var manifest = Parse(Wrap("""
            <AdaptationSet mimeType="video/mp4">
              <Representation bandwidth="1000">
                <BaseURL>video.mp4</BaseURL>
                <SegmentBase/>
              </Representation>
            </AdaptationSet>
            """));

        // The id is used to name the part on disk, so a manifest omitting it must not yield an empty
        // or duplicate name.
        var rep = Assert.Single(manifest.Video);
        Assert.False(string.IsNullOrWhiteSpace(rep.Id));
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("audio/mp4", null, "audio")]
    [InlineData("video/mp4", null, "video")]
    [InlineData("application/mp4", "audio", "audio")]   // contentType on the AdaptationSet decides
    [InlineData("application/mp4", "video", "video")]
    public void The_stream_kind_comes_from_the_mime_type_or_the_declared_content_type(
        string mime, string? contentType, string expected)
    {
        var ct = contentType is null ? "" : $" contentType=\"{contentType}\"";
        var manifest = Parse(Wrap($"""
            <AdaptationSet mimeType="{mime}"{ct}>
              <Representation id="r" bandwidth="1000">
                <BaseURL>media.mp4</BaseURL>
                <SegmentBase/>
              </Representation>
            </AdaptationSet>
            """));

        // Getting this wrong sends an audio track down the video path, so the mux pairs the wrong
        // streams (or finds none at all).
        if (expected == "audio")
        {
            Assert.Single(manifest.Audio);
            Assert.Empty(manifest.Video);
        }
        else
        {
            Assert.Single(manifest.Video);
            Assert.Empty(manifest.Audio);
        }
    }

    // ---- timelines and durations ------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_timeline_entry_with_a_repeat_count_expands_to_that_many_segments()
    {
        var manifest = Parse(Wrap("""
            <AdaptationSet mimeType="video/mp4">
              <Representation id="v" bandwidth="1000">
                <SegmentTemplate media="$Number$.m4s" timescale="1000" startNumber="1">
                  <SegmentTimeline>
                    <S t="0" d="1000" r="2"/>
                  </SegmentTimeline>
                </SegmentTemplate>
              </Representation>
            </AdaptationSet>
            """));

        // r="2" means the entry repeats twice MORE, i.e. three segments in total.
        Assert.Equal(3, Assert.Single(manifest.Video).SegmentUris.Count);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("<S t=\"0\" d=\"0\"/>")]        // zero duration
    [InlineData("<S t=\"0\" d=\"-5\"/>")]       // negative duration
    [InlineData("<S t=\"0\"/>")]                // no duration at all
    public void A_timeline_entry_without_a_usable_duration_is_refused(string entry)
    {
        // Refused rather than skipped: a non-positive or absent segment duration means the timeline
        // cannot be walked, and guessing would produce a stream with the wrong segments in it. A
        // loud failure beats a file that does not play.
        var ex = Assert.Throws<DashException>(() => Parse(Wrap($"""
            <AdaptationSet mimeType="video/mp4">
              <Representation id="v" bandwidth="1000">
                <SegmentTemplate media="$Number$.m4s" timescale="1000" startNumber="1">
                  <SegmentTimeline>{entry}</SegmentTimeline>
                </SegmentTemplate>
              </Representation>
            </AdaptationSet>
            """)));

        Assert.Contains("duration", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_negative_repeat_is_treated_as_run_to_the_end_of_the_period()
    {
        var manifest = Parse(Wrap("""
            <AdaptationSet mimeType="video/mp4">
              <Representation id="v" bandwidth="1000">
                <SegmentTemplate media="$Number$.m4s" timescale="1000" startNumber="1">
                  <SegmentTimeline>
                    <S t="0" d="1000" r="-1"/>
                  </SegmentTimeline>
                </SegmentTemplate>
              </Representation>
            </AdaptationSet>
            """));

        // r="-1" is the spec's "repeat until the period ends" — with a 10s period and 1s segments
        // that is a bounded, non-empty list rather than an infinite loop.
        var segments = Assert.Single(manifest.Video).SegmentUris;
        Assert.InRange(segments.Count, 2, 20);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_template_with_no_usable_duration_is_refused()
    {
        // Without a segment duration or a period duration there is no way to know how many segments
        // exist, so guessing would produce a truncated file.
        Assert.Throws<DashException>(() => Parse("""
            <?xml version="1.0"?>
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static">
              <Period>
                <AdaptationSet mimeType="video/mp4">
                  <Representation id="v" bandwidth="1000">
                    <SegmentTemplate media="$Number$.m4s" timescale="1000" startNumber="1" duration="0" />
                  </Representation>
                </AdaptationSet>
              </Period>
            </MPD>
            """));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_missing_timescale_defaults_to_one()
    {
        var manifest = Parse(Wrap("""
            <AdaptationSet mimeType="video/mp4">
              <Representation id="v" bandwidth="1000">
                <SegmentTemplate media="$Number$.m4s" startNumber="1" duration="2" timescale="0" />
              </Representation>
            </AdaptationSet>
            """));

        // timescale="0" would divide by zero; the parser falls back to 1 (ticks == seconds).
        Assert.NotEmpty(Assert.Single(manifest.Video).SegmentUris);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_period_duration_is_used_when_the_manifest_has_none()
    {
        var manifest = Parse("""
            <?xml version="1.0"?>
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static">
              <Period duration="PT6S">
                <AdaptationSet mimeType="video/mp4">
                  <Representation id="v" bandwidth="1000">
                    <SegmentTemplate media="$Number$.m4s" startNumber="1" duration="1" timescale="1" />
                  </Representation>
                </AdaptationSet>
              </Period>
            </MPD>
            """);

        Assert.Equal(6, manifest.DurationSeconds);
        Assert.Equal(6, Assert.Single(manifest.Video).SegmentUris.Count);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("garbage")]
    [InlineData("PT0S")]
    [InlineData("")]
    public void An_unusable_duration_falls_through_to_the_next_source(string duration)
    {
        // The manifest duration is unusable, so the period's must be taken instead.
        var manifest = Parse($"""
            <?xml version="1.0"?>
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="{duration}">
              <Period duration="PT4S">
                <AdaptationSet mimeType="video/mp4">
                  <Representation id="v" bandwidth="1000">
                    <SegmentTemplate media="$Number$.m4s" startNumber="1" duration="1" timescale="1" />
                  </Representation>
                </AdaptationSet>
              </Period>
            </MPD>
            """);

        Assert.Equal(4, manifest.DurationSeconds);
    }

    // ---- initialization segments ------------------------------------------

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("initialization")]
    [InlineData("initialisation")]  // the British spelling appears in the wild
    public void Either_spelling_of_the_initialization_attribute_is_honoured(string attribute)
    {
        var manifest = Parse(Wrap($"""
            <AdaptationSet mimeType="video/mp4">
              <Representation id="v" bandwidth="1000">
                <SegmentTemplate media="$Number$.m4s" {attribute}="init.mp4"
                                 timescale="1" startNumber="1" duration="1" />
              </Representation>
            </AdaptationSet>
            """));

        // Without the init segment the muxed output has no codec headers and will not play.
        var rep = Assert.Single(manifest.Video);
        Assert.NotNull(rep.InitSegmentUri);
        Assert.EndsWith("init.mp4", rep.InitSegmentUri);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_template_without_an_initialization_segment_simply_has_none()
    {
        var manifest = Parse(Wrap("""
            <AdaptationSet mimeType="video/mp4">
              <Representation id="v" bandwidth="1000">
                <SegmentTemplate media="$Number$.m4s" timescale="1" startNumber="1" duration="1" />
              </Representation>
            </AdaptationSet>
            """));

        Assert.Null(Assert.Single(manifest.Video).InitSegmentUri);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("Initialization")]
    [InlineData("Initialisation")]
    public void A_segment_list_takes_either_spelling_of_its_initialization_element(string element)
    {
        var manifest = Parse(Wrap($"""
            <AdaptationSet mimeType="video/mp4">
              <Representation id="v" bandwidth="1000">
                <SegmentList duration="1" timescale="1">
                  <{element} sourceURL="init.mp4"/>
                  <SegmentURL media="seg1.m4s"/>
                </SegmentList>
              </Representation>
            </AdaptationSet>
            """));

        var rep = Assert.Single(manifest.Video);
        Assert.EndsWith("init.mp4", rep.InitSegmentUri);
        Assert.Single(rep.SegmentUris);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_segment_list_initialization_without_a_source_url_is_ignored()
    {
        var manifest = Parse(Wrap("""
            <AdaptationSet mimeType="video/mp4">
              <Representation id="v" bandwidth="1000">
                <SegmentList duration="1" timescale="1">
                  <Initialization/>
                  <SegmentURL media="seg1.m4s"/>
                </SegmentList>
              </Representation>
            </AdaptationSet>
            """));

        Assert.Null(Assert.Single(manifest.Video).InitSegmentUri);
    }

    // ---- base URL resolution ----------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Segment_urls_resolve_against_the_manifest_location()
    {
        var manifest = Parse(Wrap("""
            <AdaptationSet mimeType="video/mp4">
              <Representation id="v" bandwidth="1000">
                <BaseURL>video.mp4</BaseURL>
                <SegmentBase/>
              </Representation>
            </AdaptationSet>
            """));

        // Relative to the .mpd's own directory — an absolute-from-root or host-relative answer here
        // is the classic "downloads 404s" bug.
        Assert.Equal("https://host.example/path/video.mp4",
            Assert.Single(Assert.Single(manifest.Video).SegmentUris));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_absolute_base_url_overrides_the_manifest_location()
    {
        var manifest = Parse(Wrap("""
            <AdaptationSet mimeType="video/mp4">
              <Representation id="v" bandwidth="1000">
                <BaseURL>https://cdn.example/v2/</BaseURL>
                <SegmentBase/>
                <BaseURL>ignored</BaseURL>
              </Representation>
            </AdaptationSet>
            """));

        Assert.StartsWith("https://cdn.example/v2/", Assert.Single(manifest.Video).SegmentUris[0]);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_empty_base_url_element_is_ignored()
    {
        var manifest = Parse(Wrap("""
            <AdaptationSet mimeType="video/mp4">
              <Representation id="v" bandwidth="1000">
                <BaseURL>   </BaseURL>
                <BaseURL2/>
                <SegmentTemplate media="seg.m4s" timescale="1" startNumber="1" duration="1" />
              </Representation>
            </AdaptationSet>
            """));

        Assert.StartsWith("https://host.example/path/", Assert.Single(manifest.Video).SegmentUris[0]);
    }

    // ---- template substitution --------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_unknown_placeholder_is_left_alone_rather_than_mangled()
    {
        // Leaving it visible makes a broken URL obvious; silently dropping it would produce a
        // plausible-looking URL that 404s.
        Assert.Contains("$Unknown$", MpdParser.Substitute("a/$Unknown$/b.m4s", "r", 1, 1, 1));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_double_dollar_is_an_escaped_literal()
    {
        Assert.DoesNotContain("$$", MpdParser.Substitute("a$$b", "r", 1, 1, 1));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Representation_and_bandwidth_placeholders_are_substituted()
    {
        var result = MpdParser.Substitute("$RepresentationID$/$Bandwidth$/$Number%04d$.m4s", "v1", 4200, 7, null);

        Assert.Equal("v1/4200/0007.m4s", result);
    }
}
