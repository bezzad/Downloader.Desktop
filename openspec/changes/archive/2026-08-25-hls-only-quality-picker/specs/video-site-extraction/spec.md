# video-site-extraction Specification (delta)

## Purpose

REMOVED. The HLS plugin no longer extracts media from video-site page URLs. yt-dlp/deno are gone. Page URLs are ordinary HTTP links again (or handled by a future plugin).

## REMOVED Requirements

### Requirement: The HLS plugin claims supported site page URLs

The HLS plugin SHALL NOT claim YouTube, x.com/twitter, Instagram, TikTok, or other site page URLs. `CanResolve` is true only for HLS playlist URLs (path ends `.m3u8`/`.m3u`, case-insensitive) or when an optional content-type probe reports HLS.

#### Scenario: An x.com status URL is not claimed
- **WHEN** `CanResolve` is called with `https://x.com/<user>/status/<id>`
- **THEN** it returns false

#### Scenario: A YouTube URL is not claimed
- **WHEN** `CanResolve` is called with `https://youtube.com/watch?v=…` or `https://youtu.be/…`
- **THEN** it returns false

#### Scenario: A direct .m3u8 link is still claimed
- **WHEN** `CanResolve` is called with a URL ending in `.m3u8`
- **THEN** it returns true

### Requirement: A site page URL is extracted into a downloadable plan via yt-dlp

REMOVED. No yt-dlp invocation.

### Requirement: yt-dlp is provisioned on demand, not bundled

REMOVED.
