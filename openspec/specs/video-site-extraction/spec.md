# video-site-extraction Specification

## Purpose

Extraction of downloadable media from supported video-site page URLs (e.g. x.com / twitter.com status links) via the HLS plugin, using an on-demand `yt-dlp` binary, so pasting a page link yields a real downloadable plan instead of the page HTML.
## Requirements
### Requirement: The HLS plugin claims supported site page URLs

The HLS plugin resolver SHALL claim (`CanResolve` returns true) both direct HLS links
(URLs whose path ends with `.m3u8`, case-insensitive) and **supported site page URLs**, so the host's
existing link-resolution flow routes these links to this plugin. x.com / twitter.com status URLs SHALL be
recognized; a broader set of yt-dlp-supported hosts MAY also be claimed. The check SHALL be fast and
SHALL NOT perform network I/O.

#### Scenario: An x.com status URL is claimed
- **WHEN** `CanResolve` is called with `https://x.com/<user>/status/<id>`
- **THEN** it returns true
- **AND** `CanResolve` for `https://twitter.com/<user>/status/<id>` also returns true

#### Scenario: A direct .m3u8 link is still claimed
- **WHEN** `CanResolve` is called with a URL ending in `.m3u8`
- **THEN** it returns true

#### Scenario: An unrelated link is not claimed
- **WHEN** `CanResolve` is called with a plain file URL (e.g. `https://host/file.zip`)
- **THEN** it returns false
- **AND** no network request was made

### Requirement: A site page URL is extracted into a downloadable plan via yt-dlp

When the resolver is given a supported **site page URL** (not a direct media/playlist link), it SHALL use
`yt-dlp` to extract the real media stream(s) and produce a `DownloadPlan`:
- An HLS (`.m3u8`) result SHALL be expanded through the existing HLS pipeline into segment
  `DownloadPart`s with a `Concat`/`Mux` `PostProcess`.
- A progressive or separate video+audio result SHALL produce the corresponding `DownloadPart`s with an
  ffmpeg `Mux` `PostProcess` when more than one stream must be combined.
- The plan SHALL carry a `SuggestedFileName` derived from the extracted title/format, and any request
  headers (cookies/referer) required to fetch the parts.

#### Scenario: An x.com video page resolves to downloadable parts
- **WHEN** `ResolveAsync` is called with an x.com status URL that contains a video
- **THEN** yt-dlp is invoked to extract the stream metadata
- **AND** the returned `DownloadPlan` has at least one `DownloadPart` pointing at the real media URL(s)
- **AND** a `SuggestedFileName` is set
- **AND** a `PostProcess` recipe is set when the parts must be combined (HLS concat or video+audio mux)

#### Scenario: An HLS extraction reuses the existing HLS pipeline
- **WHEN** yt-dlp reports the best format is an HLS `.m3u8`
- **THEN** the resolver parses that playlist into ordered segment parts
- **AND** the plan's `PostProcess` kind combines the segments into one playable file

#### Scenario: A direct .m3u8 link bypasses extraction
- **WHEN** `ResolveAsync` is called with a direct `.m3u8` URL
- **THEN** yt-dlp is NOT invoked
- **AND** the playlist is parsed directly into segment parts (existing HLS behavior)

### Requirement: yt-dlp is provisioned on demand, not bundled

The plugin SHALL obtain the `yt-dlp` binary by downloading the correct build for the current OS into its
`IPluginContext.DataDirectory` on first use, mirroring the ffmpeg provisioning pattern, and SHALL reuse the
cached binary on later runs. The binary SHALL be runnable behind an abstraction so it can be stubbed in
tests. Provisioning failures SHALL surface a clear, user-readable error.

#### Scenario: First use downloads yt-dlp
- **WHEN** the resolver needs yt-dlp and the binary is not present in `DataDirectory`
- **THEN** the correct OS build is downloaded into `DataDirectory`
- **AND** later resolves reuse the cached binary without re-downloading

#### Scenario: Provisioning failure is reported clearly
- **WHEN** yt-dlp cannot be downloaded or executed
- **THEN** `ResolveAsync` fails with a clear message explaining extraction is unavailable
- **AND** the error is logged via the plugin `ILogger`

### Requirement: Extraction failures degrade gracefully

The resolver SHALL throw a clear, user-readable error rather than returning an empty or invalid plan when
extraction cannot find a downloadable stream (unsupported page, private/age-gated content, or a site change
yt-dlp can no longer handle). This SHALL let the host's existing "a resolver failure does not break the
download" behavior leave the original link intact.

#### Scenario: An unsupported or empty page fails clearly
- **WHEN** `ResolveAsync` is given a supported-host URL that yields no downloadable media
- **THEN** it throws an error describing that no video was found
- **AND** it does not return a `DownloadPlan` with zero parts

#### Scenario: A private or unavailable video fails clearly
- **WHEN** the target video is private, deleted, or age-gated such that extraction is denied
- **THEN** the resolver throws an error explaining the content is unavailable

### Requirement: YouTube page URLs resolve and download, including when a signed-in session is required
Pasting a YouTube video page URL SHALL resolve to a real downloadable `DownloadPlan` and complete a
successful download, including for content that requires a signed-in session. When the app cannot obtain a
working session by reading a local browser's on-disk cookie store, it SHALL accept a live session's cookies
supplied by the browser extension for the same URL and use them instead.

#### Scenario: Public video resolves and downloads without any session
- **WHEN** a YouTube video page URL that requires no sign-in is pasted into the app
- **THEN** it resolves to a `DownloadPlan` with at least one part
- **AND** the download completes successfully

#### Scenario: Session-gated video resolves using extension-supplied cookies
- **WHEN** a YouTube video page URL that requires a signed-in session is sent from the browser extension
  together with the current session's cookies for that URL
- **AND** no local browser's on-disk cookie store can provide a working session
- **THEN** the app uses the supplied cookies to resolve and download the video successfully

#### Scenario: Cookies are never persisted beyond the attempt
- **WHEN** the app uses extension-supplied cookies to resolve a download
- **THEN** the cookie data is not written to any log
- **AND** any temporary file holding the cookies is removed after the resolve/download attempt completes,
  whether it succeeded or failed

