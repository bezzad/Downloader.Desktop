# Publishing the Downloader Browser Integration extension

Everything here is ready to submit — you just need the developer accounts (one-time). Build the
packages, then upload them in each store's dashboard.

## 1. Build the packages

```bash
./scripts/build-extension.sh
# → dist/downloader-extension-chrome.zip   (Chrome Web Store + Edge Add-ons)
# → dist/downloader-extension-firefox.zip  (Firefox AMO)
```

Bump `version` in **both** `manifest.json` and `manifest.firefox.json` before each release.

## 2. Store listing (copy/paste)

- **Name:** Downloader — Browser Integration
- **Summary (≤132 chars):** Send links and detected video/audio/HLS media straight to the Downloader desktop app.
- **Category:** Productivity
- **Description:**
  > Adds “Download with Downloader” to your browser. Right-click any link, image, video or audio to
  > send it to the Downloader desktop app, or open the popup to grab the page’s links and detected
  > media (including HLS/.m3u8 streams). Captured links are sent only to the Downloader app running on
  > your own computer — nothing is uploaded anywhere. Requires the free Downloader desktop app with
  > “Browser integration” enabled. Note: DRM/encrypted streaming sites (e.g. YouTube) are not supported.
- **Privacy policy URL:** `https://github.com/bezzad/Downloader.Desktop/blob/main/src/browser-extension/PRIVACY.md`
- **Screenshots:** capture the popup (1280×800 recommended) — paste a link, the detected-media list, the context menu.
- **Icon:** `icons/icon128.png` (already in the package).

## 3. Chrome Web Store

1. One-time: register at <https://chrome.google.com/webstore/devconsole> ($5 fee).
2. **New item** → upload `dist/downloader-extension-chrome.zip`.
3. Fill the listing (above), add screenshots, set the privacy policy URL, declare permissions
   (justify `webRequest`/host access: “detect downloadable media on the current page”; host
   `127.0.0.1` : “send links to the local desktop app”).
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
