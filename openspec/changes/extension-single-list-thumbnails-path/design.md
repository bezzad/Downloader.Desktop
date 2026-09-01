## Context

The popup builds its list in `popup.js` from items the background worker sniffed off the network
(`background.js`'s `tabMedia`), then splits them with `computeMainGroups` (`common.js`) using a
freshness window against `activeHint` — a per-tab timestamp the content script (`content.js`) posts on
`play`/`pause`/`timeupdate` plus a 2 s re-check. Both halves of that correlation are guesses:
the hint says "something is visible somewhere on the page", and the promotion rule then picks
whichever *group* happened to have the most recent network activity. On x.com the hint is regularly
stale by the time the popup asks (a finished autoplay fires no further events for a while, and the
popup's question is a one-shot), so `computeMainGroups` returns an empty set and every group — real
video included — renders under the collapsed "Other detected".

Two adjacent gaps: a row is identified only by the file name parsed out of its URL, which for a
signed CDN link is noise; and the folder a download lands in is always the app's own setting, so a
user who wants extension downloads in one folder has to go and change the app.

Constraints that shape everything below: MV3 `host_permissions` are static, so the extension can only
reach the app's pre-declared 15151–15155 range; the popup must render before any network work
(`design` precedent: probes upgrade rows in place); and anything the extension captures from a page is
potentially sensitive, so it must stay inside the extension.

## Goals / Non-Goals

**Goals:**
- One list, no hidden section, ordered so the thing a user wants is at the top by construction rather
  than by inference.
- A row that is identifiable at a glance.
- A folder the user sets once in the extension and never thinks about again.
- Less machinery than before, not more: the relevance correlation is deleted, not reworked.

**Non-Goals:**
- Guessing *which* item is "the" video by any other signal. Ordering by type replaces it outright.
- A folder picker, per-site folders, or a per-download folder override in the popup (the author
  explicitly scoped this to one editable text field prefilled from the app).
- Changing what the app does with a download, the interception decision rules, or the app's own UI.
- Sending previews or any image data to the app.

## Decisions

### 1. Type ordering is a pure function, and it is the only ordering rule

`mediaTypePriority(url)` in `common.js` maps an extension to a rank: HLS `m3u8` → 0, DASH `mpd` → 1,
`mp4` → 2, other video containers → 3, audio → 4, everything else → 5. `sortDetectedGroups(groups)`
sorts by `(priority, -size, title)` — a stable, total order with no clock and no page state in it, so
the same page always renders the same way and the whole rule is unit-testable without a browser.

Size descending as the tie-break inside a type is what makes the real video lead its own group
(a poster-sized mp4 loses to the feature). `title` last keeps the order stable for two unprobed
same-type items instead of letting `Array.sort` decide.

*Alternative considered*: keep `main` as a first-level sort key ("relevance first, then type"). Rejected
— it keeps the fragile hint alive, and a wrong hint would then reorder the list rather than merely
mislabel a section, which is a worse failure. Deleting it is the point of the fix.

### 2. Delete the relevance machinery and `content.js` with it

`computeMainGroups`, `MAIN_WINDOW_MS`, the `main` flag on items, `background.js`'s `activeHint` map and
the `activeMediaHint` message all go. `content.js`'s only job was posting that hint, so the file and
its `content_scripts` manifest entry go too — which also removes a MutationObserver and a 2 s interval
from every page the user visits.

*Alternative considered*: keep `content.js` and repurpose it for thumbnails (proactively pushing
previews to the background). Rejected: it puts a permanent script on every page to serve a UI that is
only ever looked at while the popup is open, and it needs per-tab state in a service worker that MV3
may evict at any moment.

### 3. Previews are collected on demand, by the popup, the same way "Scan page links" already works

The popup calls `api.scripting.executeScript` with a self-contained function that runs in the page and
returns, per `<video>`/`<audio>` element: its `currentSrc`/`src`, its `poster`, and a frame captured by
drawing the element into a small `<canvas>` and calling `toDataURL("image/jpeg", …)` — plus the page's
`og:image`/`twitter:image`. The canvas is capped at a thumbnail width (~160 px) so the data URL stays
a few KB, and `drawImage` is wrapped so a `SecurityError` from a tainted canvas is just a miss.

This reuses an injection path that is already proven in this codebase (and already gated on the same
permissions), needs no background state, and cannot leak: the data URL is the return value of the
popup's own `executeScript` call and dies with the popup. A page that forbids injection returns
nothing and every row falls back to a placeholder.

**Mapping a preview to a row** is deliberately two-tier, because blob:-driven MSE playback means a
DOM element usually cannot be tied to the network URLs it caused:
1. exact — an element whose `currentSrc`/`src` matches a row's URL or its `groupKey` gets that
   element's own preview;
2. page-level — every other row gets the best page-level preview (largest element's frame, else its
   poster, else `og:image`), because on a single-video page that *is* the right picture and on a feed
   page it is still a better hint than nothing.

`buildThumbnailIndex(shots, pageImage)` and `pickThumbnail(index, group)` are pure and unit-tested;
only the `executeScript` call itself is browser-bound.

### 4. A placeholder is a real element, not an absent one

Rows are a fixed-height flex layout with a 64×36 leading slot. When no preview exists the slot holds a
type letter/icon block of the same size, so the list never reflows as previews arrive and a broken
image never shows (the `<img>` is only created once a data URL or an http(s) image URL exists, and its
`onerror` swaps it back to the placeholder).

### 5. The folder is read from the app once, then owned by the extension

New app endpoint `GET /api/settings` → `{ "defaultSavePath": …, "version": … }`. It is read-only and
carries no secret (explicitly not the proxy password, cookies or headers — the local API takes those
but must never hand them back). It sits in `LocalApiService`'s existing `/api/*` switch, which
deliberately sends no CORS headers; the extension reaches it through `host_permissions`, a web page
cannot read it.

`common.js` gains `getSavePath()`/`setSavePath()` over `api.storage.local` and
`fetchAppDefaultSavePath()`. The options page prefills the field with the **saved** value when there is
one, otherwise with the app's default — so the app's default is a starting point, never something that
overwrites an edit. Nothing is written to storage until the user saves, so "never configured" stays
distinguishable from "configured to the same thing the app uses".

Every silent hand-off (`sendToAppSilently`, both its JSON-POST and GET-query forms, and
`handOffToApp`) includes `path` when a folder is configured. The legacy `/add?url=` dialog fallback is
untouched — that path exists for older apps and hands the decision to the app's Add dialog by design.

*Alternative considered*: have the app expose its whole settings object. Rejected — a settings dump is
a growing surface with no consumer, and it invites shipping something sensitive by accident.

### 6. A rejected folder is a failed send, not a silent one

The app answers `400` for a non-absolute `path`. `sendToAppSilently` already maps a non-404 error to
`"fail"`, so the popup shows "Failed" and — critically — `onDownloadCreated` returns before cancelling
the browser's own download. The existing "never cancel until the app is demonstrably fetching" chain
already covers this; the folder just becomes one more way the app can decline, which that chain was
built for.

## Risks / Trade-offs

- **Cross-origin video makes frame capture fail on exactly the sites that matter most** (x.com serves
  media from a different origin, and `crossorigin` is not set on its players) → the fallback chain is
  the real feature, not the frame: poster, then `og:image`, then placeholder. The frame grab is a
  bonus where the page happens to allow it, and the row is useful without it.
- **A page-level preview can be the wrong picture on a feed page** (several videos, one `og:image`) →
  accepted, and it is why exact matching is tried first. A shared thumbnail is a weaker hint than a
  per-item one but strictly better than a bare URL; the size and type on the row remain the precise
  information.
- **`toDataURL` on a large video costs a synchronous decode in the page** → capped canvas size, JPEG
  quality 0.6, at most a handful of elements, once per popup open. No polling, no repeat.
- **Removing "Main media" removes a signal some users may have relied on** → mitigated by the ordering
  rule putting manifests and large files first, which is what "main" was trying to approximate.
- **The folder is a typed path the extension cannot validate against the OS** (it may be a Windows
  path while the app runs elsewhere, a typo, or unwritable) → prefilling from the app makes the common
  case correct, and a bad path fails the add loudly instead of silently redirecting a file.
- **A user with a folder configured and the app's own default changed later will not see the change** →
  intended: an explicit extension setting outranks the app's default, and the field shows what will
  actually be used.

## Migration Plan

No data migration. `activeMediaHint` disappearing is invisible to the user; a stale background worker
from the previous version simply receives no such messages. The download folder starts unset, so an
updated extension behaves exactly as before until the user sets one — the same principle the
interception default follows (an update must not change how someone's browser behaves).

Rollback is a version revert: nothing persisted by this change is read by older code, and
`GET /api/settings` on an older app answers 404, which `fetchAppDefaultSavePath()` treats as "no
default available" (empty field).

## Open Questions

None blocking. Left deliberately undecided for a later pass: whether the popup should offer a
per-download folder override, and whether a page-level preview should be visually marked as
approximate (e.g. dimmed) rather than shown identically to an exact one.
