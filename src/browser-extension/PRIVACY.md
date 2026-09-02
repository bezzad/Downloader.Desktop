# Privacy Policy — Downloader Browser Integration

_Last updated: 2026-09-01_

The **Downloader Browser Integration** extension is designed to send download links to the
**Downloader** desktop application on your own computer. It is built to collect as little data as
possible.

## What the extension accesses

- **The page you're on / links you click**: when you choose "Download with Downloader" (context
  menu or popup), or when the extension detects a media stream (video/audio/HLS) on the current tab,
  it reads that URL so it can hand it to the desktop app.
- **Network requests on pages you visit**: the extension observes response headers to detect
  downloadable media (e.g. `.m3u8`, video/audio content types). This happens locally in your browser.
- **Downloads you start in the browser** (only while *Intercept browser downloads* is turned on, which
  it is **not** by default): the extension is told when the browser begins a download and reads that
  download's URL, suggested file name, size and referring page, so it can decide whether to hand it to
  the desktop app and then cancel the browser's own copy. While the setting is off, nothing about your
  downloads is read at all.
- **A frame from a video on the page you're looking at**, only while the popup is open: to show a
  thumbnail next to each detected item, the extension asks the page for a small (thumbnail-sized)
  still of each `<video>`, that player's poster image, and the page's own social-preview image. These
  images are used to draw the popup and nothing else — they are not stored, not sent to the desktop
  app, and never leave your computer. Many sites' videos block this entirely, in which case the
  poster or the page image is used instead.
- **Cookies for a link you explicitly send**: when you send a link to the app, the extension reads the
  cookies for **that exact URL only** (via the browser's `cookies` API) so that a site needing a
  signed-in session (e.g. YouTube) can be downloaded. It never reads cookies for other sites, and never
  reads them except at the moment you send a link.

## What it does with it

- An intercepted download is handed to the desktop app the same way an explicitly-sent link is: the
  URL, the file name, the referring page and that URL's cookies go to your local app so the file can be
  fetched with the same session the browser would have used. If the app cannot be reached, nothing is
  sent and the browser downloads the file itself, exactly as it would have.
- The captured URL — and, when present, the cookies for that one URL — are sent **only** to the
  Downloader desktop app running on your own machine, over a local loopback connection
  (`http://127.0.0.1:15151`, or the next port in the small declared `15151`–`15155` range when that one
  is taken by another program). Nothing is sent to the developer or any third-party server.
- Cookies are treated as secrets: the app writes them to a temporary file used only for that one
  download and **deletes it immediately afterward**, and neither the extension nor the app ever logs a
  cookie value.

## What it does NOT do

- It does **not** collect, store, or transmit your browsing history, personal data, or analytics.
- It has **no** remote servers and makes **no** third-party network calls — cookies and links go only to
  your own local app, never off your device.
- It does **not** use cookies for tracking, and does **not** read cookies except for a single URL you
  explicitly choose to send to the app.

## Permissions

- `contextMenus`, `tabs`, `scripting`, `notifications` — to add the menu, and — only when you open
  the popup — to read the active tab's links and take the thumbnail stills described above, plus show
  local status. Nothing runs on a page while the popup is closed.
- `webRequest` + host access — to detect media on the current page locally.
- `storage` — to remember a few local preferences (add silently vs open the app's Add dialog, the
  folder you want downloads saved in, the interception rules, and the app's last-known listen port).
- `downloads` — to intercept downloads you start in the browser: to be told a download began, read its
  URL/name/size/referrer, and cancel the browser's copy once the desktop app has accepted it. It reads
  nothing else about your download history, sends nothing anywhere but your own local app, and is
  **unused entirely while _Intercept browser downloads_ is off** (the default). In the install prompt
  your browser describes this as "manage your downloads".
- `cookies` — to read the cookies for a single URL you send to the app (only at that moment), so a
  site that needs a signed-in session can be downloaded. Sent only to your local app, never logged,
  and the app deletes them right after the download attempt.
- Host access to `127.0.0.1`/`localhost` (ports `15151`–`15155` only) — to talk to the desktop app.

## Contact

Questions: open an issue at <https://github.com/bezzad/Downloader.Desktop/issues>.
