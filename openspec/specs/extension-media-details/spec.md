# extension-media-details Specification

## Purpose

Rich media metadata in the browser-extension popup: probed file sizes, HLS variant parsing, quality grouping with a picker, bounded non-blocking probing, and tiny-item exclusion — so detected media reads as real, choosable downloads.
## Requirements

### Requirement: File size metadata on detected media
The extension SHALL attempt to determine the file size of each detected direct media URL via a HEAD request, falling back to a ranged GET when HEAD is unsupported, and SHALL display the size in the popup when known.

#### Scenario: Size is shown when the server reports it
- **WHEN** the popup probes a detected direct media URL and the server returns a `Content-Length` (via HEAD or a ranged GET's `Content-Range`)
- **THEN** the popup displays that size next to the item

#### Scenario: Missing size degrades gracefully
- **WHEN** neither HEAD nor a ranged GET yields a size (server refuses both, or the request errors)
- **THEN** the item is shown without a size, not with an error or a placeholder implying a known value

### Requirement: HLS variant parsing and quality grouping
When a detected URL is an HLS master playlist, the extension SHALL fetch and parse it for `#EXT-X-STREAM-INF` variant entries and present them as quality options of a single grouped item, instead of listing the master playlist as one opaque file.

#### Scenario: Master playlist expands to quality options
- **WHEN** the popup probes a detected `.m3u8` URL that is a master playlist with multiple `#EXT-X-STREAM-INF` variants
- **THEN** the popup shows one card for that video with a quality selector listing each variant's resolution (or bandwidth when resolution is absent)

#### Scenario: Unparseable or single-variant playlists still work
- **WHEN** a detected `.m3u8` URL fails to parse or has no `#EXT-X-STREAM-INF` entries
- **THEN** the item is shown as a single row using today's plain behavior

### Requirement: Grouping of same-video quality variants
Direct-file media URLs that share a directory and basename apart from a trailing quality token SHALL be grouped into one card with a quality picker; ambiguous cases SHALL remain ungrouped rather than merged incorrectly.

#### Scenario: Differently-quality-named files group together
- **WHEN** two detected URLs differ only by a trailing quality token (e.g. `_720p` vs `_1080p`) in an otherwise identical path
- **THEN** the popup shows one card with both qualities selectable

#### Scenario: Unrelated files are never merged
- **WHEN** two detected URLs do not match the conservative same-basename-minus-quality-token pattern
- **THEN** they are shown as separate items, never combined into one card

### Requirement: Non-blocking, bounded probing
Metadata probing SHALL run with a concurrency cap and a per-request timeout, and SHALL NOT delay the popup's initial render of the detected media list.

#### Scenario: Popup renders immediately, then upgrades
- **WHEN** the popup opens with detected media already known
- **THEN** the plain list renders immediately and each row is upgraded in place as its probe resolves

#### Scenario: A hanging probe does not block others or the UI
- **WHEN** one item's probe does not respond within the timeout
- **THEN** that probe is aborted, the item keeps its unprobed appearance, and other items' probes and the popup remain responsive

### Requirement: Implausibly tiny probed items are excluded
Once a probe confirms a detected item's size, the extension SHALL exclude it from the popup if that size falls below a small fixed floor, since a response that tiny is not usable media (a tracking beacon or empty init segment); an item whose size is not yet known SHALL NOT be excluded.

#### Scenario: A confirmed-tiny item is dropped
- **WHEN** a probe confirms a detected item's size is below the minimum media size floor
- **THEN** the item does not appear in the popup

#### Scenario: An unprobed item is never pre-emptively excluded
- **WHEN** an item's size has not yet been probed
- **THEN** the item is shown normally, since its size is unknown, not confirmed tiny

### Requirement: Download sends the selected quality
When a grouped card's quality picker has more than one option, the Download action SHALL send the currently selected variant's URL, not a fixed default.

#### Scenario: Selecting a different quality changes what downloads
- **WHEN** the user changes a grouped card's quality selector and clicks Download
- **THEN** the extension sends the URL corresponding to the newly selected quality to the desktop app
