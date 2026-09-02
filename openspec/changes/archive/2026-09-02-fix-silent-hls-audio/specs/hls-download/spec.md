## ADDED Requirements

### Requirement: A variant's separate audio rendition is downloaded and muxed

The resolver SHALL download a variant's separate audio track: when a master playlist declares
`#EXT-X-MEDIA:TYPE=AUDIO` renditions and the selected `#EXT-X-STREAM-INF` variant is served without
its own audio, it SHALL also fetch the matching audio rendition's media playlist and emit its
segments as a **second stream group** of the `Concat` recipe (video group first), so assembly
concatenates each group and muxes them into one playable file. A variant that carries its own audio SHALL still produce a single-group recipe,
byte-for-byte as before.

The audio rendition SHALL be chosen by proof, never by assumption:
- the variant's `AUDIO` group, preferring that group's `DEFAULT=YES` entry;
- a rendition with no `URI` SHALL be ignored (its audio is muxed into the variant already);
- a variant naming no group SHALL take the master's default audio rendition **only** when its
  `CODECS` attribute lists no audio codec; an absent or empty `CODECS` SHALL NOT trigger a guess.

#### Scenario: A video-only variant gains its audio track
- **WHEN** a master whose 1080p variant declares `CODECS="avc1.640028",AUDIO="aud"` is resolved, and
  the `aud` group has a `DEFAULT=YES` rendition with a URI
- **THEN** the plan contains the variant's segments followed by the audio rendition's segments
- **AND** the recipe describes two stream groups with those segment counts
- **AND** assembly muxes the concatenated video with the concatenated audio

#### Scenario: A self-contained variant is unchanged
- **WHEN** a master's chosen variant declares `CODECS="avc1.640028,mp4a.40.2"` and names no audio group
- **THEN** only that variant's segments are planned and the recipe describes a single stream

#### Scenario: An audio rendition without a URI is not downloaded twice
- **WHEN** the audio rendition for the variant's group declares no `URI`
- **THEN** nothing extra is downloaded and the recipe describes a single stream

### Requirement: Assembly states which streams it is combining

When two streams are muxed, the muxer SHALL map them explicitly (the first input's video stream and
the second input's audio stream) rather than relying on the tool's default stream selection, which
picks one stream per type across **all** inputs and can therefore drop the real audio in favour of a
stray track on the video input. AAC arriving in an MPEG-TS intermediate SHALL be converted to its
MP4-legal form; MP4/fMP4 audio SHALL NOT be filtered.

#### Scenario: The intended audio stream is the one in the output
- **WHEN** a concatenated video stream and a concatenated audio stream are muxed
- **THEN** the output carries the video from the first input and the audio from the second

#### Scenario: Transport-stream AAC is made MP4-legal
- **WHEN** the audio intermediate is an MPEG-TS (or raw AAC) file
- **THEN** the ADTS-to-ASC bitstream conversion is applied
- **AND** it is NOT applied when the audio intermediate is MP4/fMP4

### Requirement: fMP4 intermediates are labelled as MP4

When a media playlist carries an `#EXT-X-MAP` init segment, its concatenated intermediate SHALL be
named with an `.mp4` extension, since labelling fragmented MP4 data as `.ts` misleads the assembling
tool's container probing.

#### Scenario: An fMP4 stream assembles
- **WHEN** a playlist with an `#EXT-X-MAP` init segment is resolved
- **THEN** the recipe's intermediate extension is `.mp4`
