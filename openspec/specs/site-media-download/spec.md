# site-media-download Specification

## Purpose
Download the real media behind a video page (the optional `com.bezzad.site-media` plugin): a page URL
is extracted into the stream(s) that actually carry the video, offered per quality, and — when the
page only serves video and audio separately — muxed into one playable file.

## Requirements
### Requirement: A muxed download prefers audio the output container can play

The resolver SHALL prefer audio the output container can actually play: when a video page resolves to
separate video and audio streams that are muxed into an MP4, it SHALL pick an audio stream whose
codec MP4 players decode natively (AAC, ALAC, AC-3, MP3) over both the extraction tool's own default pick and a higher-bitrate stream in a foreign codec
(Opus, Vorbis). Opus SHALL still be used when it is the only audio available — a download is never
refused over this.

The judgement SHALL be made on the stream's reported codec (falling back to its container), never on
the downloaded file's name: extracted stream URLs carry no file extension.

#### Scenario: AAC wins over a higher-bitrate Opus stream
- **WHEN** a page offers a video-only stream, a 130 kbps Opus audio stream (also the tool's own pick)
  and a 129 kbps AAC audio stream
- **THEN** the AAC stream is the one downloaded and muxed

#### Scenario: Opus is used when there is nothing else
- **WHEN** a page's only audio stream is Opus
- **THEN** that stream is downloaded and muxed

