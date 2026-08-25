## Why

Issue #9 (reporter `ray2me123`, 2026-08-24, app v2.5.0 / extension 1.2.2): clicking a normal
download link lets the browser download the file itself instead of handing it to Downloader. The
right-click "Download with Downloader" menu works, and sniffed media reaches the app, so the
integration is plainly connected — only automatic interception is missing. Reproduced across Chrome,
Chromium and Edge, on `.exe`, `.zip` and other ordinary files, on several sites.

He is not misconfigured, and there is no setting he missed: **the extension has never implemented
interception.** It declares no `downloads` permission and never touches the `chrome.downloads` API;
there is no `onDeterminingFilename` listener anywhere. He also noticed the options page is
unavailable — likewise true, the manifest declares none.

This matters beyond one report: taking over browser downloads is the single behavior people expect
from a download manager, and it is what makes the app worth leaving running. It is also where the
per-download request context (issue #7) finally pays off — an intercepted download is exactly the
case that needs the page's cookies, referer and headers, or every gated file we take over will fail
in a way the browser would not have.

## What Changes

- The extension intercepts browser downloads: it cancels the browser's own download and hands the
  URL to the app, together with the request context needed to fetch it.
- Interception is a **user-controlled setting**, not an unconditional takeover: an on/off switch, and
  rules for what to leave to the browser (a minimum size, an extension allow/deny list, a site
  exclusion list). Ordinary browsing — a PDF opening inline, a small file — must not become worse.
- When the app is unreachable, the browser's own download proceeds untouched. Interception must never
  be a way to lose a file.
- An **options page**, which the extension currently lacks entirely, hosts those settings.
- The extension sends the **referer and page headers** with every hand-off, not just cookies. This is
  the extension half of issue #7 that the app-side change deliberately left out of scope.
- The `downloads` permission is added to both manifests, and `PRIVACY.md` explains what it is used
  for, since a permission increase can gate a store review.

## Capabilities

### New Capabilities
- `browser-download-interception`: taking over downloads the browser starts — when to intercept, what
  to leave alone, what happens when the app is unreachable, and the user's control over all of it.

### Modified Capabilities
- `browser-extension`: hand-offs carry the page's request context (referer and headers), not cookies
  alone; the extension gains a settings surface.

## Impact

- `src/browser-extension/manifest.json` and `manifest.firefox.json` — the `downloads` permission and
  an `options_ui` entry. A permission increase affects store review for both.
- `src/browser-extension/background.js` — the `downloads.onCreated`/`onDeterminingFilename` listener
  and the cancel-and-hand-off flow.
- `src/browser-extension/common.js` — `sendToApp` gains referer/headers; interception rules as pure,
  testable predicates.
- New `options.html`/`options.js`/`options.css`; `popup.js` gains a link to them.
- `src/browser-extension/common.test.js` and the Playwright suite in `e2e/`.
- `PRIVACY.md`, `README.md`, `PUBLISHING.md`.
- **Depends on `issue7-followup-fixes`** for the app side: an intercepted download is added through
  `/api/add`, and it needs the request context to arrive intact.
