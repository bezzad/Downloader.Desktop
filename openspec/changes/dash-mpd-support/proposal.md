# Proposal — dash-mpd-support

Issue #5 (@ray2me123, split out of issue #4): *"besides HLS, how does it handle DASH streams, which is quite
different from HLS?"*

Today the answer is: it doesn't. A `.mpd` manifest is not recognized by any resolver, so pasting one
downloads the XML manifest file itself instead of the video, and the browser extension never offers a DASH
stream it sniffed on a page.

## Why

MPEG-DASH is the other half of adaptive streaming on the web. Sites that serve DASH are common enough that
"we support adaptive streaming" is misleading while only HLS works. The pieces the app needs are already
built for HLS — a resolver that expands a manifest into segment parts, the multi-part plan runner, an
ffmpeg post-processor, dependency self-healing — so DASH is mostly a second manifest parser plugged into
the same pipeline, not new infrastructure.

## What changes

DASH lands **inside the existing HLS plugin** (`com.bezzad.hls`), renamed to *Streaming media (HLS & DASH)*:

- A `DashResolver` claims `.mpd` links, parses the manifest and produces one `DownloadPart` per segment
  for the chosen video representation plus its matching audio representation.
- Quality picker: the video representations become `LinkVariant`s (height / bitrate / estimated size), so
  the Add window offers the same picker HLS master playlists already get. Default = highest bitrate.
- Assembly: the post-process recipe grows a notion of **streams**, so the processor concatenates the video
  segments and the audio segments separately and then muxes them into one file with ffmpeg (`-c copy`).
  Single-stream recipes (every HLS plan today) keep the exact current behaviour.
- The browser extension detects `.mpd` alongside `.m3u8`.

### Why one plugin, not two

A separate DASH plugin would duplicate ~400 lines of ffmpeg/binary-dependency plumbing and make every user
download a second ~80 MB ffmpeg into a second plugin data directory, for the same job. Extending the
existing plugin means one install, one update, one ffmpeg. The plugin keeps its id (`com.bezzad.hls`) so
installed copies simply see a normal version update.

## Scope

**In:** static (VOD) manifests, `SegmentTemplate` with `$Number$`/`$Time$` (with or without
`SegmentTimeline`), `SegmentList`, `SegmentBase`, and plain `BaseURL` single-file representations;
`BaseURL` inheritance down the MPD/Period/AdaptationSet/Representation chain; video+audio muxing;
audio-only and video-only manifests; per-request headers stamped onto every segment.

**Out (detected and refused with a clear message, not silently mis-downloaded):**

- **Live / dynamic manifests** (`type="dynamic"`) — a moving target with no defined end; a different
  feature (record-a-live-stream), not a download.
- **DRM-protected streams** (`ContentProtection` / Widevine / PlayReady / CENC) — decryption keys are the
  point of DRM; refusing with an honest message is the only correct behaviour.

**Also out of this change:** subtitle tracks, multi-language audio selection, and trick-mode
representations — the picker chooses a video quality and pairs it with the best audio.

## Risks

- Real-world MPDs vary a lot. Mitigated by parsing against committed fixtures for each addressing mode and
  by falling back to the manifest's declared duration when a segment count can't be derived exactly.
- Concatenated fMP4 segments must be fed to ffmpeg in a container it probes correctly; the intermediate
  file extension is part of the recipe rather than hard-coded to `.ts`.
- The end-to-end result (a playable file from a real DASH site) cannot be verified headlessly — it needs
  the author to run one real `.mpd` download.
