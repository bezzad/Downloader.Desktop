# Publishing the Downloader Browser Integration extension

**All stores are a manual dashboard upload.** Bump `version` in **both** `manifest.json` and
`manifest.firefox.json`, push to `develop`, and upload the zip from the release page.

- **Firefox (AMO)**: a manual upload at
  <https://addons.mozilla.org/en-US/developers/addon/downloader-browser-integration/versions>.
  The old `extension.yml` workflow (automated `web-ext sign` submission) was **removed on
  2026-08-24** — it had failed on every run since 2026-07-07 because AMO validation rejected the
  package with *"A content script defined in the manifest could not be found at `content.js`"*.
  Use `downloader-extension-firefox.zip` from the release page if you do publish a version.
- **Chrome Web Store / Edge Add-ons**: still a dashboard upload (steps below). Both extension zips
  are also attached to every GitHub Release (`release.yml`), so grab
  `downloader-extension-chrome.zip` from the release page — it's the exact build to upload.

## 1. Build the packages (manual/local fallback)

```bash
./scripts/build-extension.sh
# → dist/downloader-extension-chrome.zip   (Chrome Web Store + Edge Add-ons)
# → dist/downloader-extension-firefox.zip  (Firefox AMO — normally submitted by CI)
```

## 2. Store listing (copy/paste)

- **Name:** Downloader — Browser Integration
- **Summary (≤132 chars):** Send links and detected video/audio/HLS media straight to the Downloader desktop app.
- **Category:** Productivity
- **Description:**
  > Adds “Download with Downloader” to your browser. Right-click any link, image, video or audio to
  > send it to the Downloader desktop app, or open the popup to grab the page’s links and detected
  > media (including HLS/.m3u8 streams). Captured links are sent only to the Downloader app running on
  > your own computer — nothing is uploaded anywhere. Requires the free Downloader desktop app with
  > “Browser integration” enabled. Optionally, it can take over downloads the browser starts and hand
  > them to the app instead — this is off until you turn it on, and you choose which file types, sizes
  > and sites it applies to. If the app isn’t running, the browser downloads the file as usual.
  > Note: DRM/encrypted streaming sites (e.g. YouTube) are not supported, and running another
  > intercepting download manager alongside this one is not supported.
- **Privacy policy URL:** `https://github.com/bezzad/Downloader.Desktop/blob/main/src/browser-extension/PRIVACY.md`
- **Screenshots:** capture the popup (1280×800 recommended) — paste a link, the detected-media list, the context menu.
- **Icon:** `icons/icon128.png` (already in the package).

## 3. Chrome Web Store

1. One-time: register at <https://chrome.google.com/webstore/devconsole> ($5 fee).
2. **New item** → upload `dist/downloader-extension-chrome.zip`.
> **1.6.0 changes no permissions** — it only fixes how an intercepted download's type is decided, which
> link is handed to the app, and what the popup says when the app cannot be found. If 1.5.0 (which
> introduced the `downloads` permission) has not been published yet, the note below still applies to the
> submission; if it has, this is an ordinary update.
>
> **The `downloads` permission — expect a slower review on the submission that introduces it.** Both stores scrutinise a
> permission increase, and “manage your downloads” reads alarmingly in the install prompt. Justify it
> as: “take over downloads the user starts in the browser and hand them to the user’s own desktop app;
> used only while the user enables the interception setting, which is off by default; nothing is sent
> anywhere but the user’s own machine.” Do **not** bundle this version with an urgent fix — ship urgent
> fixes before or after it, not in the same submission.

3. Fill the listing (above), add screenshots, set the privacy policy URL, declare permissions
   (justify `webRequest`/host access: “detect downloadable media on the current page”; host
   `127.0.0.1` ports `15151`–`15155`: “send links to the local desktop app; the app falls back
   within this small fixed port range when the default port is taken by another program”).
4. Submit for review.

## 4. Microsoft Edge Add-ons (same package)

1. One-time: register at <https://partner.microsoft.com/dashboard/microsoftedge> (free).
2. **New extension** → upload the same `downloader-extension-chrome.zip` → fill listing → submit.

## 5. Firefox Add-ons (AMO)

1. One-time: sign in at <https://addons.mozilla.org/developers/> (free).
2. **Submit a New Add-on** → upload `dist/downloader-extension-firefox.zip`.
3. AMO may ask for the source — this repo folder `src/browser-extension` is the source (no build step).
4. Fill the listing + privacy policy URL → submit for review.

## 6. After approval

Update the README install links with the published store URLs (placeholders are marked there).
