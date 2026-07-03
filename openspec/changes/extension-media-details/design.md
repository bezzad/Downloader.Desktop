# Design — extension-media-details

## Context

Today `background.js`'s `onHeadersReceived` sniffer adds `{ url, type }` to a per-tab `Map` the instant a matching response is seen; `popup.js` renders one row per map entry using only the URL's basename. There's no size, no resolution, and no relationship between a master `.m3u8` and its variant playlists/segments — each looks like an unrelated file. This proposal adds a probing step and a grouping step between capture and render, while keeping the extension dependency-free (no bundler/build step per the existing repo convention).

## Goals / Non-Goals

**Goals:**
- Show file size and (for video) resolution per detected item, when obtainable without heavy cost.
- Collapse an HLS master playlist's variants, and same-basename quality variants of direct files, into one card with a quality picker.
- Keep the popup responsive: probing must not block first paint, and a slow/failed probe degrades to today's plain row rather than hanging the UI.

**Non-Goals:**
- No thumbnails/preview frames (needs frame decoding — out of scope for a manifest-only extension).
- No DASH (`.mpd`) parsing — only HLS (`.m3u8`), matching the existing `MEDIA_EXTENSIONS` support.
- No format conversion, no site-specific extractors, no DRM/YouTube support (existing project policy, unchanged).
- No change to how a chosen URL is delivered to the app (`/api/add` stays exactly as-is).

## Decisions

1. **Probe on demand from the popup, not in the background sniffer.** The sniffer (`onHeadersReceived`) fires on every response and must stay cheap; issuing a `HEAD`/manifest-fetch per hit there would multiply network calls for pages with many resources the user never opens the popup for. Instead, `getMedia` (already the popup's fetch-detected-media message) triggers probing for the tab's currently-known URLs, lazily, right before render. Alternative considered — probe in the background as items are captured — rejected: wastes bandwidth/battery for the common case of a popup never opened.

2. **`HEAD` first, fall back to a 1-byte ranged `GET`.** Some CDNs reject `HEAD` (405) or omit `Content-Length` on it; a `Range: bytes=0-0` GET reliably returns `Content-Range: bytes 0-0/<total>` when the server supports ranges (most media CDNs do). If neither yields a size, the item shows no size (not "0 B" or an error) — matches "best-effort, never block."

3. **HLS variant parsing is a pure text parser**, not a full HLS library: fetch the `.m3u8` body, regex/line-scan for `#EXT-X-STREAM-INF:...RESOLUTION=WxH,BANDWIDTH=N` followed by the variant URI on the next line. This covers the vast majority of real playlists (single-level master → variants) without pulling in an HLS parsing dependency (repo convention: extension has zero build step / zero npm deps at runtime). Multi-level (master → variant → sub-variant) playlists are out of scope; they fall back to the master URL as a single ungrouped item.

4. **Grouping key**: 
   - HLS: the master playlist URL is the group key; its parsed variants become the group's quality options (label = `RESOLUTION` or `~bandwidth kbps` if resolution is absent).
   - Direct files: group by (directory path + basename with any trailing quality token like `_720p`/`-1080`/`.hd` stripped) — a conservative regex, so unrelated files never merge; a same-name-different-quality miss just shows two separate cards (current behavior), which is safe by default.

5. **Size for HLS groups**: exact total size isn't knowable without fetching every segment. Show a `~` estimated size computed as `(sampled first-segment size) × (segment count from the variant playlist)` when a variant playlist is cheaply fetchable; otherwise omit size for that variant. Labelled with `~` so it's clearly an estimate, never presented as exact.

6. **Concurrency + timeout**: probes run through a small helper that caps concurrent fetches (e.g. 4) and aborts each via `AbortController` after ~2.5s, so a slow/hanging origin can't stall the popup. Popup renders the plain (unprobed) list immediately, then upgrades rows in place as probes resolve — no spinner-blocking.

7. **UI**: replace the flat `<ul><li>` rows with a `<ul><li class="card">` per group: title line (grouped label or filename), a `<select>` of quality options (single option = today's look, no dropdown shown), a size line, and the existing Download button — now sending the `<select>`'s currently chosen URL instead of the row's fixed URL.

## Risks / Trade-offs

- [Extra network requests per popup-open] → Bounded by concurrency cap + timeout; only runs for items currently in the tab's detected list (typically single-digit counts), and only while the popup is open.
- [Grouping false-positives/negatives on unusual URL schemes] → The basename-quality-token regex is deliberately conservative (see Decision 4); a miss degrades to the current one-row-per-URL behavior, never a wrong merge.
- [HLS estimated size could mislead] → Always prefixed with `~`; omitted entirely rather than guessed when even one segment fetch fails.
- [Some sites block cross-origin HEAD/Range via CORS from the extension's fetch] → Best-effort: on fetch failure/opaque response, the item silently keeps no size/resolution (today's plain row), consistent with the non-blocking goal.

## Open Questions

_None outstanding — scope (metadata + grouping only, no thumbnails/DASH/conversion) was confirmed with the author before this design._
