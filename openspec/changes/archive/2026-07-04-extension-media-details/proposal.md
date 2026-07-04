# Extension media details (size, resolution, quality grouping)

## Why

The browser extension currently lists detected media as bare `filename + extension` rows (`popup.js`'s `render()`), one row per sniffed URL. Comparable extensions (e.g. Video DownloadHelper) show file size, resolution, and group a video's multiple quality/rendition URLs into one card with a quality picker — this is the single biggest visible gap the author identified when comparing extensions. Closing part of that gap (metadata + grouping) makes the popup genuinely useful for choosing what to download instead of guessing from a URL.

Two related, real-world problems compound the same "which one is the file I want" confusion and are folded into this change:
- **Signal-to-noise on media-heavy pages**: a single X.com post page can sniff 120+ URLs (segments, thumbnails, unrelated feed items, tracking pixels) with no indication of which one is the video the user is actually looking at.
- **Silent failure on YouTube-style sites**: the popup shows "No media detected" with no explanation, which looks like a bug rather than the documented, intentional limitation (adaptive/DRM streaming via MSE has no single fetchable URL).

## What Changes

- **Metadata probing**: for each detected media URL, the background worker issues a lightweight `HEAD` (or ranged `GET` for servers that reject HEAD) to read `Content-Length` (→ file size) and confirm `Content-Type`; for `.m3u8` playlists it fetches and parses the manifest to extract `#EXT-X-STREAM-INF` variant lines (resolution + bandwidth) instead of treating the master playlist as one opaque file.
- **Quality grouping**: HLS variants of the same master playlist, and same-basename direct files differing only by a resolution/quality marker in the URL, are grouped into a single popup card with a quality dropdown instead of N separate rows.
- **Popup UI update**: each card shows the grouped video's best-guess title (page title fallback), size (or "~size" for HLS, computed from a sampled segment × segment count when exact size isn't knowable), resolution/quality options, and a single Download button that sends the selected variant's URL.
- **Probing is bounded and best-effort**: requests run with a short timeout and a small concurrency cap; a probe failure just falls back to today's plain filename/extension row instead of blocking the popup.
- **"Main media" vs. "Other detected" split**: a lightweight content script tracks which `<video>`/`<audio>` element is currently visible/playing on the page; sniffed URLs that correlate with that element's active window are shown in a "Main media" section (usually just the one group the user came for), everything else collapses into an "Other detected (N)" section the user can expand. This directly targets the 120-links-on-X.com problem without hiding data.
- **Known-unsupported-site messaging**: when the popup finds nothing eligible on a hostname known to stream via MSE/DRM (YouTube, Netflix, etc.), it shows "This site streams video in a format Downloader can't capture directly" instead of the generic empty state — turning a confusing silent failure into an expected, explained one. This does **not** make YouTube downloadable; it only fixes the user-facing confusion.
- Deliberately **not** implemented (author-confirmed scope): thumbnail/preview frames, DASH manifest parsing, site-specific extractors, format conversion, or any attempt to actually capture YouTube/DRM media — these stay out of scope; DRM/YouTube-style protected streams remain unsupported per existing project policy.

## Capabilities

### New Capabilities
- `extension-media-details`: per-item size/resolution metadata probing and quality-variant grouping in the browser extension's detected-media list.
- `extension-media-relevance`: main-vs-other media triage (visible/playing-element correlation) and known-unsupported-site messaging in the popup.

### Modified Capabilities

_None — no existing spec covers the extension's popup/detection behavior yet (browser-extension spec only covers the silent-add capability); this is additive to the extension's UI, not a change to a documented requirement._

## Impact

- `src/browser-extension/background.js`: media map gains probed fields; grouping logic for HLS variants and same-basename quality files; correlates captured URLs with the content script's "active media element" signal.
- `src/browser-extension/common.js`: new probing/parsing helpers (HEAD fetch, m3u8 variant parse), kept pure/testable where possible.
- `src/browser-extension/popup.js` + `popup.html`/`popup.css`: card-based rendering with quality picker; Main/Other sections; known-unsupported-site empty state.
- New `src/browser-extension/content.js` (or similar): a small content script (uses the existing `scripting` permission — no new permission needed) that tracks page `<video>`/`<audio>` visibility/playing state and reports it to the background worker.
- No changes to the desktop app, the local API, or the .NET plugin SDK (`Downloader.Desktop.Plugins.Abstractions`) — **this work is fully independent of that repo/SDK.** The plugin SDK's `ILinkResolver` is a still-unbuilt, in-app .NET interface for a future HLS/yt-dlp *download* plugin; the extension's HLS parsing here is plain client-side JS solely for *displaying* quality options before handing one URL to `/api/add`, with no shared code or dependency either direction.
- Extension version bump (manifest.json + manifest.firefox.json) triggers the existing release/AMO automation.
