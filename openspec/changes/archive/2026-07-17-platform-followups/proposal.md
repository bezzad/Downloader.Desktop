# Platform follow-ups: X.com variants/download, winget Start-menu shortcut, MS Store prep

## Why
(a) Pasting an x.com link shows the Website plugin's "Offline copy (.zip)" variant mixed into the HLS plugin's quality list, and X.com quality resolution/download fails (YouTube works). (b) A winget install succeeds but the app is findable nowhere (portable installs create no Start-menu entry). (c) The author asked about the Microsoft Store — possible, but gated on his Partner Center account.

## What Changes
- Variant lookup: only the CLAIMING resolver's variants are offered — a fallback resolver's extras must not leak into another plugin's list; fix the X.com quality extraction (yt-dlp needs cookies/args for x.com — diagnose and fix like the YouTube deno case).
- On Windows, the app self-registers a Start-menu shortcut on first run (idempotent, removable), so winget/portable installs are launchable from Start.
- MS Store: record the author-gated plan (Partner Center account → MSIX submission); no submission yet.

## Capabilities
### Modified
- `link-variants`: variants come only from the claiming resolver.
- `video-site-extraction`: x.com links resolve qualities and download.
- `platform-distribution`: Windows Start-menu shortcut self-registration.
