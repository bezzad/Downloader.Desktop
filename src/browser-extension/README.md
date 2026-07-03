# Downloader — Browser Integration (extension)

A cross-browser **Manifest V3** extension that hands download links and detected media to the
**Downloader** desktop app.

## Features

- **Context menu** — right-click a link, image, video or audio → **“Download with Downloader”**.
  Right-click a text selection → **“Download selected links with Downloader”**.
- **Popup** — paste a link, send the **detected media** for the current tab, or **scan the page**
  for downloadable links, then send one or all to the app.
- **Media capture** — watches network responses and surfaces **video / audio / HLS (`.m3u8`)**
  streams, with a badge count on the toolbar icon.
- **Size, resolution and quality picker** — each detected item is probed for its file size, and an
  HLS master playlist expands into a quality dropdown (resolution or bitrate per option) instead of
  one opaque `.m3u8` row. Probing never blocks the popup: it renders immediately, then upgrades
  rows in place as results arrive.
- **Main media vs. Other detected** — on media-heavy pages (a social-media post with dozens of
  segment/thumbnail requests) the video you're actually viewing is promoted to a **Main media**
  section; everything else collapses into an expandable **Other detected (N)** — nothing is hidden,
  just triaged.
- **Known-unsupported-site message** — on sites that stream via MSE/DRM with no fetchable file URL
  (YouTube, Netflix, …), the popup explains why nothing was found instead of showing a blank list
  that looks broken.
- **Auto-send** — every captured link is forwarded to the app's local listener. By default it is
  **added silently and starts downloading** (the app's `/api/add` endpoint); untick
  **“Add silently (no dialog)”** in the popup to review each link in the app's Add dialog
  (`/add?url=…`) instead. On an app version without the API the extension falls back to the
  dialog automatically.

> **Not supported:** YouTube and other DRM/encrypted streaming sites — they don't expose a direct,
> fetchable media URL, so there's nothing the engine can download.

## Requirements

1. The **Downloader desktop app** must be running.
2. **Settings → Browser extension & local API** must be on in the app (it opens the local listener
   on port `15151`). It is enabled by default.

## Load it for testing (unpacked)

### Chrome / Edge
1. Go to `chrome://extensions` (or `edge://extensions`).
2. Turn on **Developer mode**.
3. **Load unpacked** → select this `browser-extension` folder.

### Firefox
1. Copy `manifest.firefox.json` over `manifest.json` (Firefox reads `manifest.json`):
   ```bash
   cp manifest.firefox.json manifest.json
   ```
   (Keep a backup of the Chrome `manifest.json` — they differ only in the `background` shape and the
   Firefox `browser_specific_settings` block.)
2. Go to `about:debugging#/runtime/this-firefox` → **Load Temporary Add-on** → pick `manifest.json`.

## How it talks to the app

The extension only needs the app's loopback endpoints:

```
GET http://127.0.0.1:15151/api/add?url=<url-encoded link>   # silent add (default)
GET http://127.0.0.1:15151/add?url=<url-encoded link>       # open the Add dialog instead
GET http://127.0.0.1:15151/ping                              # reachability check
```

These are served by `Services/LocalApiService.cs` in the desktop app (see `docs/local-api.md` in the
main repo for the full API). No other ports, servers or accounts are involved.

## Files

| File | Role |
|------|------|
| `manifest.json` | Chrome/Edge MV3 manifest (service-worker background) |
| `manifest.firefox.json` | Firefox MV3 manifest (scripts background + gecko id) |
| `common.js` | Shared helpers: media detection, `sendToApp()`, size/HLS probing, grouping |
| `background.js` | Context menus, response sniffing, badge, message handler, probing coordinator |
| `content.js` | Tracks the visible/playing `<video>`/`<audio>` element for Main-vs-Other triage |
| `popup.html` / `popup.css` / `popup.js` | Toolbar popup UI (grouped cards, quality picker) |
| `icons/` | Toolbar/store icons (16/48/128) |

Run the unit tests (pure helpers in `common.js`) with `node --test src/browser-extension/common.test.js`.

## Publishing (later)

Store submission needs the author's developer accounts and is **not** done here:

- **Chrome Web Store / Edge Add-ons** — zip this folder and upload in the respective dashboards.
- **Firefox (AMO)** — submit the Firefox-manifest build at `addons.mozilla.org`.

Bump `version` in both manifests for each release.
