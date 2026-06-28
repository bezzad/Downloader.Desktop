## ADDED Requirements

### Requirement: HLS link recognition

The plugin's `ILinkResolver` SHALL recognize HLS playlist links so the host routes them to the HLS resolver instead of the direct-file engine.

#### Scenario: URL path ends with .m3u8
- **WHEN** `CanResolve` is called with a URL whose path ends in `.m3u8` (case-insensitive, ignoring query string)
- **THEN** it returns `true`

#### Scenario: Content type indicates HLS
- **WHEN** `CanResolve` is called with a URL that does not end in `.m3u8`, but the resolver is configured with a content-type probe that reports `application/vnd.apple.mpegurl` / `application/x-mpegURL`
- **THEN** it returns `true` (the probe is optional and injected, so the default `CanResolve` stays network-free)

#### Scenario: Non-HLS URL is ignored
- **WHEN** `CanResolve` is called with an ordinary direct-file URL (e.g. `.zip`, `.mp4`) that is not an HLS playlist
- **THEN** it returns `false`

### Requirement: M3U8 playlist parsing

The plugin SHALL parse M3U8 playlists into a structured, ordered representation, resolving relative URIs against the playlist URL.

#### Scenario: Master playlist variant selection
- **WHEN** a master playlist containing multiple `#EXT-X-STREAM-INF` variants is parsed
- **THEN** the highest-`BANDWIDTH` variant (or the variant matching a provided quality hint) is selected and its media playlist URL is resolved to an absolute URL

#### Scenario: Media playlist segment list
- **WHEN** a media playlist is parsed
- **THEN** the result is an ordered list of segment URIs with each relative URI resolved to an absolute URL against the playlist's base URL

#### Scenario: Encryption key parsing
- **WHEN** a media playlist contains an `#EXT-X-KEY` tag with `METHOD=AES-128`, a `URI`, and an optional `IV`
- **THEN** the parsed result carries the key URI and IV (defaulting the IV from the segment media sequence when absent) for the segments it applies to

#### Scenario: Initialization segment
- **WHEN** a media playlist contains an `#EXT-X-MAP` init segment
- **THEN** the init segment URI is captured and ordered before the media segments

#### Scenario: Garbled or empty playlist
- **WHEN** an empty playlist, or text that is not a valid `#EXTM3U` playlist, is parsed
- **THEN** a clear, specific error is raised rather than producing an empty or partial plan silently

### Requirement: Resolve to a download plan

`ResolveAsync` SHALL fetch and expand an HLS link into a `DownloadPlan` whose parts are the playlist's segments and whose post-process step describes how to assemble them.

#### Scenario: Plan from a media playlist
- **WHEN** `ResolveAsync` is given an HLS link served by a loopback server
- **THEN** it returns a `DownloadPlan` with one `DownloadPart` of `Kind=Segment` per segment in playlist order, a `PostProcess` of `Kind=Concat` whose `Recipe` (JSON) encodes the segment order plus any AES key/IV, and a `SuggestedFileName` derived from the URL (defaulting to a `.mp4`/`.ts` name)

#### Scenario: Master playlist is followed to its media playlist
- **WHEN** `ResolveAsync` is given a master playlist URL
- **THEN** it selects the best variant, fetches that media playlist, and produces the segment-based plan from it

### Requirement: AES-128 segment decryption

The post-processor SHALL decrypt AES-128-encrypted segments correctly before assembly.

#### Scenario: Known-vector round trip
- **WHEN** a segment encrypted with a known AES-128 key and IV is decrypted by the plugin
- **THEN** the output bytes exactly match the original plaintext segment

#### Scenario: Unencrypted segments pass through
- **WHEN** a playlist has no `#EXT-X-KEY`
- **THEN** segments are concatenated without any decryption step

### Requirement: Assemble segments into one playable file

`IPostProcessor.ProcessAsync` (Concat) SHALL produce a single playable output file from the downloaded (and decrypted) segments in playlist order.

#### Scenario: Concatenate and remux
- **WHEN** the ordered segment files are post-processed
- **THEN** they are concatenated in order and remuxed into an MP4 container via ffmpeg with stream copy (`-c copy`), yielding one output file at the requested output path

#### Scenario: Progress is reported
- **WHEN** post-processing runs
- **THEN** progress is reported through the provided `IProgress<double>` from 0 toward 1.0

### Requirement: ffmpeg provisioned on first use

The plugin SHALL obtain ffmpeg without bundling it in the installer, behind an abstraction that tests can stub.

#### Scenario: Download on first use
- **WHEN** post-processing needs ffmpeg and no ffmpeg binary is present in the plugin's data directory
- **THEN** a static ffmpeg build for the current OS is downloaded into `ctx.DataDirectory` and reused on subsequent runs

#### Scenario: ffmpeg is stubbable in tests
- **WHEN** the plugin is constructed with a stub `IFfmpeg`
- **THEN** post-processing logic can be tested without invoking a real ffmpeg binary

### Requirement: Loadable as a host plugin

The built plugin SHALL be a loadable DLL that the host's plugin loader recognizes as an `IDownloaderPlugin`.

#### Scenario: Loads in a host-mirroring load context
- **WHEN** the plugin DLL is loaded in a collectible `AssemblyLoadContext` that resolves `Downloader.Desktop.Plugins.Abstractions` from the default context (mirroring the host loader)
- **THEN** the loaded type is assignable to `IDownloaderPlugin` and exposes its `Id`, `Name`, `Version`, `Author`, and `Description`
