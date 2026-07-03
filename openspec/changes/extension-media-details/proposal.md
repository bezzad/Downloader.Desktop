# Extension media details (size, resolution, quality grouping)

## Why

The browser extension currently lists detected media as bare `filename + extension` rows (`popup.js`'s `render()`), one row per sniffed URL. Comparable extensions (e.g. Video DownloadHelper) show file size, resolution, and group a video's multiple quality/rendition URLs into one card with a quality picker — this is the single biggest visible gap the author identified when comparing extensions. Closing part of that gap (metadata + grouping) makes the popup genuinely useful for choosing what to download instead of guessing from a URL.

## What Changes

- **Metadata probing**: for each detected media URL, the background worker issues a lightweight `HEAD` (or ranged `GET` for servers that reject HEAD) to read `Content-Length` (→ file size) and confirm `Content-Type`; for `.m3u8` playlists it fetches and parses the manifest to extract `#EXT-X-STREAM-INF` variant lines (resolution + bandwidth) instead of treating the master playlist as one opaque file.
- **Quality grouping**: HLS variants of the same master playlist, and same-basename direct files differing only by a resolution/quality marker in the URL, are grouped into a single popup card with a quality dropdown instead of N separate rows.
- **Popup UI update**: each card shows the grouped video's best-guess title (page title fallback), size (or "~size" for HLS, computed from a sampled segment × segment count when exact size isn't knowable), resolution/quality options, and a single Download button that sends the selected variant's URL.
- **Probing is bounded and best-effort**: requests run with a short timeout and a small concurrency cap; a probe failure just falls back to today's plain filename/extension row instead of blocking the popup.
- Deliberately **not** implemented (author-confirmed scope): thumbnail/preview frames, DASH manifest parsing, site-specific extractors, format conversion — these stay out of scope; DRM/YouTube-style protected streams remain unsupported per existing project policy.

## Capabilities

### New Capabilities
- `extension-media-details`: per-item size/resolution metadata probing and quality-variant grouping in the browser extension's detected-media list.

### Modified Capabilities

_None — no existing spec covers the extension's popup/detection behavior yet (browser-extension spec only covers the silent-add capability); this is additive to the extension's UI, not a change to a documented requirement._

## Impact

- `src/browser-extension/background.js`: media map gains probed fields; grouping logic for HLS variants and same-basename quality files.
- `src/browser-extension/common.js`: new probing/parsing helpers (HEAD fetch, m3u8 variant parse), kept pure/testable where possible.
- `src/browser-extension/popup.js` + `popup.html`/`popup.css`: card-based rendering with quality picker replacing the flat list.
- No changes to the desktop app or the local API — the extension still calls `/api/add` with whichever single URL the user picks.
- Extension version bump (manifest.json + manifest.firefox.json) triggers the existing release/AMO automation.
