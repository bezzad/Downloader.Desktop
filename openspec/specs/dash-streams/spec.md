# dash-streams Specification

## Purpose
Downloading MPEG-DASH (.mpd) streams via the Streaming media plugin (com.bezzad.hls, which also handles
HLS): expanding a static manifest into segments, offering its video representations as qualities, combining
the separate video and audio streams into one playable file, refusing live and DRM-protected streams with a
reason, and detecting .mpd links in the browser extension.

## Requirements
### Requirement: MPEG-DASH manifests are downloadable
The app SHALL download an MPEG-DASH stream from its `.mpd` manifest, producing a single playable media file.
Video and audio delivered as separate representations SHALL be combined into that one file.

#### Scenario: A DASH manifest is downloaded as a playable file
- **WHEN** the user adds a link to a static MPEG-DASH manifest
- **THEN** the app downloads the segments of the selected video representation and of the matching audio
  representation
- **AND** combines them into one media file without re-encoding

#### Scenario: A manifest with only one media type
- **WHEN** the manifest offers only video representations, or only audio representations
- **THEN** the app downloads that single stream and produces a playable file from it

#### Scenario: Segments are fetched with the download's request context
- **WHEN** the download carries per-request headers, cookies or a referer
- **THEN** the manifest request and every segment request SHALL be made with them

### Requirement: DASH qualities are offered before downloading
The app SHALL list the video qualities a DASH manifest offers so the user can choose one before the download
starts, and SHALL default to the highest-quality representation.

#### Scenario: The user picks a quality
- **WHEN** a DASH manifest offers several video representations
- **THEN** the Add window lists them with their resolution or bitrate and an estimated size
- **AND** the highest-bitrate representation is pre-selected

#### Scenario: A manifest with a single quality
- **WHEN** a DASH manifest offers one video representation
- **THEN** no quality choice is presented and the download proceeds directly

#### Scenario: A chosen quality survives a retry
- **WHEN** a download of a chosen DASH quality is retried
- **THEN** the same quality is resolved again

### Requirement: Unsupported DASH streams are refused with a reason
The app SHALL NOT present a live or DRM-protected DASH stream as a downloadable file. It SHALL stop with a
message that says why.

#### Scenario: A live stream
- **WHEN** the manifest describes a live (dynamic) stream
- **THEN** the download fails with a message stating that live streams are not supported

#### Scenario: A protected stream
- **WHEN** the manifest declares content protection
- **THEN** the download fails with a message stating that protected streams cannot be downloaded

### Requirement: DASH links are detected in the browser
The browser extension SHALL detect `.mpd` manifests on a page the same way it detects `.m3u8` playlists, and
SHALL offer each detected manifest as one downloadable item.

#### Scenario: A DASH manifest is offered
- **WHEN** a page loads an MPEG-DASH manifest
- **THEN** the extension lists it as a single media item that can be sent to the app

