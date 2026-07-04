# Design — extension-media-details

## Context

Today `background.js`'s `onHeadersReceived` sniffer adds `{ url, type }` to a per-tab `Map` the instant a matching response is seen; `popup.js` renders one row per map entry using only the URL's basename. There's no size, no resolution, and no relationship between a master `.m3u8` and its variant playlists/segments — each looks like an unrelated file. This proposal adds a probing step and a grouping step between capture and render, while keeping the extension dependency-free (no bundler/build step per the existing repo convention).

Two additional real-world symptoms of the same "no relevance signal" gap:
- On media-heavy feed pages (e.g. an X.com post page), `onHeadersReceived` accumulates every media-looking response for the tab's lifetime with no decay or ranking — segments from the viewed video, thumbnails, and unrelated feed items all land in the same flat list, observed at 120+ entries.
- On YouTube, adaptive playback goes through Media Source Extensions: the `<video>` element's `src` is a `blob:` URL fed by JS-level `fetch()`/`XMLHttpRequest` calls to signed, short-lived, per-byte-range `googlevideo.com` URLs. These are not stable, single-file downloadable URLs (this is *why* the project already excludes YouTube/DRM sites — see `common.js`'s existing comment and the repo's design notes), so "no media detected" there is **correct behavior**, but the popup gives no indication that this is expected versus a bug.

**Independence from the .NET plugin SDK**: `Downloader.Desktop.Plugins.Abstractions`'s `ILinkResolver` is an in-app interface for a *future, still-unbuilt* HLS/yt-dlp download plugin (see `docs/plugins-architecture.md`'s "NOT YET" list) — it resolves a link to download parts inside the desktop app's engine. This proposal's HLS parsing is unrelated: plain client-side JS in the browser extension that reads a playlist purely to populate a quality dropdown in the popup, before handing one plain URL to `/api/add` exactly as today. No code, types, or process boundary is shared between the two; naming them separately here is to prevent future confusion, not because of any technical coupling.

## Goals / Non-Goals

**Goals:**
- Show file size and (for video) resolution per detected item, when obtainable without heavy cost.
- Collapse an HLS master playlist's variants, and same-basename quality variants of direct files, into one card with a quality picker.
- Keep the popup responsive: probing must not block first paint, and a slow/failed probe degrades to today's plain row rather than hanging the UI.
- On media-heavy pages, surface the media the user is actually looking at as "Main media" and demote the rest to a collapsed, still-accessible "Other detected" bucket.
- On known-unsupported (MSE/DRM) sites, explain the empty state instead of leaving it silent.

**Non-Goals:**
- No thumbnails/preview frames (needs frame decoding — out of scope for a manifest-only extension).
- No DASH (`.mpd`) parsing — only HLS (`.m3u8`), matching the existing `MEDIA_EXTENSIONS` support.
- No format conversion, no site-specific extractors, no DRM/YouTube support (existing project policy, unchanged) — the relevance work explains the YouTube limitation, it does not lift it.
- No change to how a chosen URL is delivered to the app (`/api/add` stays exactly as-is).
- No dependency on, or code sharing with, `Downloader.Desktop.Plugins.Abstractions` / the .NET plugin SDK (see Context) — this change is entirely within `src/browser-extension`.

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

8. **Active-media signal via a content script**, not more network heuristics. A small injected script (`content.js`, loaded via the existing `scripting` permission — matches the pattern already used by `popup.js`'s `scanPageLinks` `executeScript` call) observes the page's `<video>`/`<audio>` elements with `IntersectionObserver` (visibility) and their `play`/`pause`/`timeupdate` events, and posts `{ activeMediaHint }` to the background worker whenever the "most relevant" element changes, plus on a 2s periodic re-check. Alternative approaches considered — inferring relevance purely from network patterns (e.g. "most-requested host") — rejected as too fragile across sites; a direct DOM signal is simpler and more accurate for "what is the user looking at."
   **Revision (v1.2.1, real-world testing on x.com):** v1.2.0 required the element to be *currently playing* (`!el.paused`) to qualify at all. In practice X.com autoplays an inline video once and leaves it paused on its last frame — a common, expected state, not an edge case — so the hint was never sent and "Main media" stayed empty even while the user was looking straight at the video. `mostRelevant()` now also accepts a visible element that is paused but has loaded metadata (`readyState >= 1` or `currentTime > 0`), ranked below an actively-playing one. The periodic re-check exists because such a static element never fires another play/pause/timeupdate event to keep the hint fresh on its own.

9. **Correlation, not certainty**: pure function `computeMainGroups(items, hint, nowMs, windowMs)` in `common.js` (unit-tested without a browser). This is a heuristic, not a guarantee — mistakes fail safe into "Other detected," never by hiding an item entirely.
   **Revision (v1.2.1):** the original algorithm compared each item's *own* capture time against the hint, independently per item. Since a resource is recorded in `tabMedia` only once (its `capturedAt` is stamped at first sighting and never refreshed on repeat/cached requests), a video whose segments were fetched a while before the user opened the popup — including the exact "paused on last frame" case above — could never match, even with Decision 8's fix. The corrected algorithm: when the hint is fresh (within `windowMs` of *now*), promote whichever group(s) had the most recent network activity on the page (`max` capturedAt per group, then any group within `windowMs` of that overall max) — a blob: URL still prevents a precise DOM-to-network mapping, so this remains "the freshest thing on the page while something is confirmed visible," not a guaranteed-correct match. `groupKey`/HLS-master grouping from Decision 4 is used as before so a whole quality-group promotes together.

10. **Popup layout**: a "Main media" section renders promoted group(s) (usually zero or one); everything else renders under a collapsed, count-labelled `<details>` "Other detected (N)" — expandable, not deleted, so a determined user can still reach any item.
    **Revision (v1.2.1, real-world testing on YouTube):** v1.2.0 only showed the known-unsupported-site message when *zero* groups were detected — but YouTube's own UI/notification sound effects (`success.mp3`, `no_input.mp3`, …) matched `MEDIA_EXTENSIONS`/content-type sniffing and counted as "detections," so the real bug (nothing downloadable was ever found) was masked by irrelevant junk being listed as if downloadable. On a hostname matching the known-unsupported list (`youtube.com`, `netflix.com`, `disneyplus.com`, `primevideo.com` — extendable), the popup now **always** shows the explanatory message and suppresses the list outright, regardless of incidental detections — nothing found there is ever the protected content the user wants. Non-matching hostnames are unaffected (generic empty state only when actually empty).

11. **No claim of certainty about "the" main video**: on a feed page with several visible videos (e.g. multiple autoplaying posts), more than one group may be promoted — the goal is trimming obvious noise (thumbnails, off-screen items, trackers), not guaranteeing exactly one result.

12. **Noise floor for implausibly tiny "media"** (added v1.2.1, real-world testing on x.com): probing surfaced legitimate-looking `.m3u8`/`.mp4` entries at 786–988 bytes — clearly tracking beacons or empty init segments, not content anyone would download. `isPlausibleMediaSize(size)` in `common.js` rejects any *probed* size below `MIN_MEDIA_BYTES` (8 KB); an unprobed item (`size == null`) always passes, so nothing is ever hidden before its size is actually known. Applied in `popup.js`'s `buildGroups()`: an option below the floor is dropped, and a group left with zero options is dropped entirely.

13. **Variant/segment dedup for a real master** (added v1.2.2, caught by the new Playwright e2e suite — see `e2e/README.md`). A real HLS master's variant playlists AND their individual `.ts` segments are, like the master itself, independently visible to the browser's network stack and get sniffed as their OWN top-level items — without dedup, a single video produced 2-3+ near-duplicate cards (the master plus each variant/segment). Once a master's variants are parsed, `estimateHlsSize` also returns each variant's resolved segment URLs; `popup.js`'s `buildGroups()` collects every variant URI and segment URL across all real (`probed.kind === "hls"`) masters into one set and deletes any top-level group whose key matches — they're already represented by the master's own quality picker. A variant/media playlist that fails `parseHlsMaster` (not a real master) is unaffected and still shown as its own item, filtered only by the size floor from Decision 12 (its own tiny size, e.g. a ~140-byte media playlist, generally removes it anyway).

## Risks / Trade-offs

- [Extra network requests per popup-open] → Bounded by concurrency cap + timeout; only runs for items currently in the tab's detected list (typically single-digit counts), and only while the popup is open.
- [Grouping false-positives/negatives on unusual URL schemes] → The basename-quality-token regex is deliberately conservative (see Decision 4); a miss degrades to the current one-row-per-URL behavior, never a wrong merge.
- [HLS estimated size could mislead] → Always prefixed with `~`; omitted entirely rather than guessed when even one segment fetch fails.
- [Some sites block cross-origin HEAD/Range via CORS from the extension's fetch] → Best-effort: on fetch failure/opaque response, the item silently keeps no size/resolution (today's plain row), consistent with the non-blocking goal.
- [Active-media heuristic mis-promotes or under-promotes on an unusual page] → Fails safe: a wrongly-demoted item is still one click away in "Other detected," never hidden or deleted; a wrongly-promoted item is just an extra card, not a functional break.
- [Known-unsupported-site list goes stale as sites change] → It's a short, easily-editable array in one file; being incomplete only means the generic empty state shows instead of the explained one — never a false claim that an actually-working site is unsupported (the list only gates the *message*, not detection itself).
- [Content script adds a small per-page-load cost] → `IntersectionObserver` + a handful of media-element event listeners is negligible; the script only runs when `<video>`/`<audio>` elements exist (early-exit otherwise).

## Open Questions

_None outstanding — scope (metadata + grouping + main/other triage + unsupported-site messaging; no thumbnails/DASH/conversion/YouTube support) was confirmed with the author before this design._
