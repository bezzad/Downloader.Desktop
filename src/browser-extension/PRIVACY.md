# Privacy Policy — Downloader Browser Integration

_Last updated: 2026-06-20_

The **Downloader Browser Integration** extension is designed to send download links to the
**Downloader** desktop application on your own computer. It is built to collect as little data as
possible.

## What the extension accesses

- **The page you're on / links you click**: when you choose "Download with Downloader" (context
  menu or popup), or when the extension detects a media stream (video/audio/HLS) on the current tab,
  it reads that URL so it can hand it to the desktop app.
- **Network requests on pages you visit**: the extension observes response headers to detect
  downloadable media (e.g. `.m3u8`, video/audio content types). This happens locally in your browser.

## What it does with it

- The captured URL is sent **only** to the Downloader desktop app running on your own machine, over a
  local loopback connection (`http://127.0.0.1:15151`). Nothing is sent to the developer or any
  third-party server.

## What it does NOT do

- It does **not** collect, store, or transmit your browsing history, personal data, or analytics.
- It has **no** remote servers and makes **no** third-party network calls.
- It does **not** use cookies or tracking.

## Permissions

- `contextMenus`, `tabs`, `scripting`, `notifications` — to add the menu, read the active tab's links
  on request, and show local status.
- `webRequest` + host access — to detect media on the current page locally.
- Host access to `127.0.0.1`/`localhost` — to talk to the desktop app.

## Contact

Questions: open an issue at <https://github.com/bezzad/Downloader.Desktop/issues>.
