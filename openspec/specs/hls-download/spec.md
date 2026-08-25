# hls-download Specification

## Purpose

Download a direct HLS (`.m3u8` / `.m3u`) stream: list qualities from a master playlist, download the chosen or best rendition as segments, assemble (AES-128 decrypt + concat + ffmpeg remux) into a playable file.

## Requirements

### Requirement: Master playlists offer a quality picker

When the resolver is given a master playlist URL, `GetVariantsAsync` SHALL return one variant per `#EXT-X-STREAM-INF` rendition, ordered highest-bandwidth first, with that first entry marked `IsDefault`. Labels SHALL include the height when `RESOLUTION` is present (e.g. `1080p`) and an approximate size when duration can be read from the best media playlist (`bandwidth/8 × duration`). A media playlist (no STREAM-INF) SHALL return null — there is no choice, and default download still works.

#### Scenario: Master playlist lists qualities with default on best
- **WHEN** a master playlist with 360p/720p/1080p variants is entered in Add
- **THEN** the picker shows three qualities, 1080p pre-checked, each with an approximate size when duration is known

#### Scenario: Media playlist has no picker
- **WHEN** a media playlist URL (segments only, no STREAM-INF) is entered
- **THEN** `GetVariantsAsync` returns null and Download uses the segment plan as today

### Requirement: Resolve honors the chosen quality, default is best

`ResolveAsync` SHALL follow the master playlist to the rendition whose id matches `ResolveOptions.VariantId`. When `VariantId` is null or unknown, it SHALL pick the highest-bandwidth rendition (`Best()`). The resulting plan is one `DownloadPart` per media segment (plus optional init) with a Concat post-process.

#### Scenario: User picks 360p
- **WHEN** resolve runs with `VariantId` equal to that variant's bandwidth id
- **THEN** the plan's segment URLs come from the 360p media playlist, not the 1080p one

#### Scenario: Default download with nothing extra checked
- **WHEN** resolve runs with no `VariantId` (plain add)
- **THEN** the plan uses the highest-bandwidth media playlist
