# Tasks — extension-media-details

## 1. Probing helpers (common.js)

- [ ] 1.1 `probeSize(url)`: HEAD request reading `Content-Length`; on 405/missing header, fall back to a `Range: bytes=0-0` GET reading `Content-Range`'s total; return `null` on any failure (never throws)
- [ ] 1.2 `parseHlsMaster(url)`: fetch the playlist text; regex-scan for `#EXT-X-STREAM-INF:...` + following URI line; return `[{ uri, resolution, bandwidth }]` (empty array if unparseable/no variants)
- [ ] 1.3 `estimateHlsSize(variantUrl)`: fetch the variant playlist, read its segment list + fetch the first segment's size via `probeSize`, return `segmentCount * firstSegmentSize` or `null`
- [ ] 1.4 `groupKey(url)`: conservative same-basename-minus-quality-token key (strip trailing `_720p`/`-1080`/`.hd`-style tokens only; anything else returns the full URL as its own unique key)
- [ ] 1.5 `runProbesBounded(items, { concurrency, timeoutMs })`: small scheduler wrapping the above with an `AbortController` per request and a concurrency cap

## 2. Background grouping (background.js)

- [ ] 2.1 Extend the per-tab media map's stored shape to support a `group` field (computed via `groupKey`/HLS-master-URL) without breaking existing consumers
- [ ] 2.2 Expose a `probeMedia` message handler that runs `runProbesBounded` over the requested tab's current items and returns enriched results (size/resolution/variants) — kept separate from `getMedia` so the popup can render fast first, then request probes

## 3. Popup rendering (popup.js/html/css)

- [ ] 3.1 Render grouped cards: title, size line (with `~` prefix for HLS estimates), a `<select>` for quality when >1 variant, Download button
- [ ] 3.2 Immediate render from `getMedia` (today's data), then call `probeMedia` and upgrade rows in place as results arrive — never block first paint
- [ ] 3.3 Download button sends the `<select>`'s currently chosen URL (or the item's single URL when ungrouped)
- [ ] 3.4 CSS for the card layout (quality select, size line) consistent with the existing popup style

## 4. Active-media relevance (content script + triage)

- [ ] 4.1 New `content.js`: `IntersectionObserver` over the page's `<video>`/`<audio>` elements + `play`/`pause`/`timeupdate` listeners; posts `{ activeMediaHint }` to the background worker when the most-relevant element changes; early-exits when no media elements exist
- [ ] 4.2 Register `content.js` in both manifests (`content_scripts`, matches `<all_urls>`, runs at `document_idle`) — no new permission needed (`scripting`/host permissions already present)
- [ ] 4.3 Background: track the latest `activeMediaHint` per tab; when grouping a sniffed URL (via `groupKey`/HLS-master from task 2.1), tag the group `main: true` if it was captured within ~3s of a matching hint, else `main: false`
- [ ] 4.4 Popup: render a "Main media" section for `main: true` groups, and a collapsed `<details>` "Other detected (N)" for the rest; both reuse the card rendering from task 3.1
- [ ] 4.5 `KNOWN_UNSUPPORTED_HOSTS` list (youtube.com, netflix.com, disneyplus.com, primevideo.com — extendable) in `common.js`; popup shows an explanatory message instead of the generic empty state when zero groups exist AND the tab's hostname matches

## 5. Tests & manual verification

- [ ] 5.1 Node-runnable unit tests (reuse the existing `module.exports` pattern in `common.js`) for `parseHlsMaster`, `groupKey`, the HEAD/Range fallback logic (mock `fetch`), and the known-unsupported-hostname matcher
- [ ] 5.2 Manual verification: load unpacked in Chrome + Firefox against a real HLS test page and a page with multiple direct-file qualities; confirm grouping, quality switching, and graceful degradation when a probe is blocked (test via a CORS-restricted URL)
- [ ] 5.3 Manual verification on a media-heavy feed page (e.g. x.com post): confirm the played/visible video lands under "Main media" and the rest collapse under "Other detected (N)"
- [ ] 5.4 Manual verification on youtube.com: confirm the explanatory unsupported-site message appears instead of a blank list

## 6. Docs & wrap-up

- [ ] 6.1 Update `src/browser-extension/README.md` to describe the new size/quality display, Main/Other sections, and the unsupported-site message
- [ ] 6.2 Bump `manifest.json` + `manifest.firefox.json` version (triggers the existing release-asset + AMO automation); commit and push to `develop`
