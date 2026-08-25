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

## Outcome (archived 2026-08-25)

Implemented on `develop` in commit `988561b`; specs synced in `a10d25d`. 25 of 26 tasks complete.
Extension `1.2.2` → `1.3.0`. Verified green: 54 `node --test` unit tests (20 new, covering the whole
rule set and the hand-off), 11 Playwright e2e specs run with `--workers=1`, and a clean
`dotnet build -t:Rebuild` (`0 Warning(s)`) confirming the app side was untouched.

The dependency landed first: `issue7-followup-fixes` was archived the same day, so the app accepts
the context an intercepted download carries and reports back how much of it arrived.

Replies to issue #9 (comment `5409598598`) and issue #10 (comment `5409599728`) were shown in full
and posted only after an explicit OK.

**Left unverified — one task, and it needs a human at a browser:**

- **6.4 — the author's manual check.** Load the unpacked extension in Chrome, download a real file
  from a real site with interception on, then with it off, then with the app closed. Everything that
  *can* be checked headlessly is: the decide-then-cancel ordering, the rules, the hand-off payload,
  and the "app refused it, so the browser keeps the file" path all have e2e coverage against a stub
  app. What that cannot prove is behaviour against a real site and a real app on a real desktop.

**Two things the next session should know:**

- **Interception is off by default and the file-type rule is an ALLOW list** (archive/installer types).
  A download of an unlisted type is left to the browser by design — this is the first thing to check
  against any "it didn't intercept my file" report, and the options page says so in those words.
- **A small file can finish before the hand-off completes**, leaving the browser's copy done *and* the
  app fetching it; the extension detects the failed cancel and says "Downloading twice". A non-zero
  minimum size would remove the class of problem, but the default was deliberately set to 0.

**Not yet released**, and the version that carries this adds the `downloads` permission — expect a
slower store review, and do not bundle it with an urgent fix (`PUBLISHING.md` carries the same note).
