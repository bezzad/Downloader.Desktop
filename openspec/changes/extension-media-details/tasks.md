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

## 4. Tests & manual verification

- [ ] 4.1 Node-runnable unit tests (reuse the existing `module.exports` pattern in `common.js`) for `parseHlsMaster`, `groupKey`, and the HEAD/Range fallback logic (mock `fetch`)
- [ ] 4.2 Manual verification: load unpacked in Chrome + Firefox against a real HLS test page and a page with multiple direct-file qualities; confirm grouping, quality switching, and graceful degradation when a probe is blocked (test via a CORS-restricted URL)

## 5. Docs & wrap-up

- [ ] 5.1 Update `src/browser-extension/README.md` to describe the new size/quality display
- [ ] 5.2 Bump `manifest.json` + `manifest.firefox.json` version (triggers the existing release-asset + AMO automation); commit and push to `develop`
