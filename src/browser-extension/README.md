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
- **Auto-send** — every captured link is forwarded to the app's local listener
  (`http://127.0.0.1:15151/add?url=…`), which opens the Add dialog pre-filled.

> **Not supported:** YouTube and other DRM/encrypted streaming sites — they don't expose a direct,
> fetchable media URL, so there's nothing the engine can download.

## Requirements

1. The **Downloader desktop app** must be running.
2. Enable **Settings → Browser integration** in the app (it opens the local listener on port `15151`).

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

The extension only needs the app's loopback endpoint:

```
GET http://127.0.0.1:15151/add?url=<url-encoded link>
```

This is served by `Services/BrowserIntegrationService.cs` in the desktop app. No other ports, servers
or accounts are involved.

## Files

| File | Role |
|------|------|
| `manifest.json` | Chrome/Edge MV3 manifest (service-worker background) |
| `manifest.firefox.json` | Firefox MV3 manifest (scripts background + gecko id) |
| `common.js` | Shared helpers: media detection + `sendToApp()` |
| `background.js` | Context menus, response sniffing, badge, message handler |
| `popup.html` / `popup.css` / `popup.js` | Toolbar popup UI |
| `icons/` | Toolbar/store icons (16/48/128) |

## Publishing (later)

Store submission needs the author's developer accounts and is **not** done here:

- **Chrome Web Store / Edge Add-ons** — zip this folder and upload in the respective dashboards.
- **Firefox (AMO)** — submit the Firefox-manifest build at `addons.mozilla.org`.

Bump `version` in both manifests for each release.
