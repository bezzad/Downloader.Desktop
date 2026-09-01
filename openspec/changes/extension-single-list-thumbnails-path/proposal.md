## Why

On an x.com video page the popup shows nothing under **Main media** and the user has to expand
**Other detected** to find the video they are looking straight at. The promotion rule requires a
*fresh* visibility hint from the content script (within a 3 s window) at the exact moment the popup
asks; on a feed page whose player has finished autoplaying, or when the hint lands a moment late,
every item — including the real video — is demoted. The split therefore hides the one thing the user
opened the popup for, and the "relevance" it buys is a guess that fails on the most common video site
the extension is used on.

Two things are also missing once the list is flat: nothing on a row says *which* video a link is
(the file name of a signed CDN URL is meaningless), and every send still leaves the app to decide
where the file goes, so a user who wants downloads in one specific folder has to touch the app.

## What Changes

- **BREAKING (popup UI)**: the **Main media** / **Other detected (N)** split is removed. All detected
  media appears in one list, with no collapsed section and no relevance promotion.
- The single list is **sorted by media type**: adaptive manifests first (`.m3u8`, then `.mpd`), then
  `.mp4`, then other video containers, then audio, then anything else; ties broken by size (largest
  first) so the real video leads its own type group.
- The content-script visibility hint (`activeMediaHint`) and the `main` flag it drove are removed —
  nothing consumes them once the split is gone.
- **New**: each row shows a **thumbnail** on the left. The content script captures a small JPEG frame
  from the on-page `<video>` via a canvas, falling back to the element's `poster`, then the page's
  `og:image`/`twitter:image`, and finally a file-type icon drawn in the popup. Frames never leave the
  machine — they are passed to the popup through the extension's own messaging.
- **New**: a **download folder** field on the extension's options page. The extension reads the app's
  configured default save path and prefills the field with it; the user edits the text. Every send
  (popup, context menu, interception) includes that folder, so the app writes there without asking.
  No file picker — the browser cannot offer one for an arbitrary OS folder.
- **New app endpoint**: `GET /api/settings` returns the app's default save path (and app version) so
  the extension has something to prefill with. Read-only; no secrets.
- Extension version 1.6.1 → **1.7.0**.

## Capabilities

### New Capabilities
- `extension-media-thumbnails`: how a detected item gets a visual preview — frame capture, the
  fallback chain, size/cost limits, and the placeholder when nothing is available.
- `extension-download-folder`: the extension-side download folder — prefilled from the app's default,
  editable, and applied to every hand-off.

### Modified Capabilities
- `extension-media-relevance`: the "Main media vs Other detected" requirement is replaced by a single
  type-sorted list. The known-unsupported-site requirement is unchanged.
- `local-api`: adds a read-only `GET /api/settings` endpoint exposing the default save path.
- `browser-extension`: an add carries the extension's configured folder as `path`.

## Impact

- `src/browser-extension/`: `popup.js`, `popup.html`, `popup.css` (single list + thumbnail column),
  `content.js` (thumbnail capture replaces the relevance hint), `background.js` (thumbnail store per
  tab, `main`/`activeHint` removal, folder on every send), `common.js` (type ordering, thumbnail
  helpers, folder storage + `/api/settings` read, `path` on sends), `options.html`/`options.js`
  (folder field), both manifests (version).
- `src/browser-extension/common.test.js` (unit) and `e2e/` (Playwright) — the relevance specs are
  replaced by ordering specs; new specs for thumbnails and the folder.
- `src/Downloader.Desktop/Services/LocalApiService.cs`: new `settings` route.
- `src/Downloader.Desktop.Tests/`: a test for the new endpoint.
- No change to the app's UI, download engine, or plugins.
