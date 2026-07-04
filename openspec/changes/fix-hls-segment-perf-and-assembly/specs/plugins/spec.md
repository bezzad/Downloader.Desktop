## MODIFIED Requirements

### Requirement: Plan execution downloads segment parts efficiently
When executing a resolved multi-part plan, the app SHALL download parts marked as segments (or parts
smaller than a small-part threshold) with a single connection/chunk instead of the user's full
multipart configuration. Large non-segment parts keep the normal multipart behavior.

#### Scenario: HLS segments are single-chunk
- **WHEN** a plan with `PartKind.Segment` parts (e.g. an HLS stream's `.ts` segments) runs
- **THEN** each segment downloads with one chunk / one connection
- **AND** the per-segment overhead of multipart chunking (multiple range requests per tiny file) does not occur

### Requirement: Assembled output carries a standard media extension
The plan runner SHALL hand post-processors a temporary output path whose file extension is a standard
media extension (extension last), and SHALL normalize a playlist-derived final name (`.m3u8`/`.m3u`)
to a media container extension when the plan includes a post-process step.

#### Scenario: ffmpeg can choose a muxer for the temp output
- **WHEN** a Mux/Concat post-processor (ffmpeg-based) receives the temp output path
- **THEN** the path ends in a standard media extension (e.g. `video.assembling.mp4`), never in a bare `.assembling`

#### Scenario: Playlist name becomes a media name
- **WHEN** the chosen final name ends in `.m3u8`/`.m3u` and the plan has a post-process step
- **THEN** the assembled file is saved with a `.mp4` extension (or the plugin's suggested media extension)
