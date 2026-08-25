## Context

The extension (`src/browser-extension/`, MV3, cross-browser via `manifest.json` +
`manifest.firefox.json`) does context menus, `webRequest.onHeadersReceived` media sniffing, and a
popup. It has never intercepted browser downloads: neither manifest declares the `downloads`
permission, `background.js` contains no `chrome.downloads` reference at all, and no repo doc claims
otherwise. The reporter's diagnosis in issue #9 was correct on every point, including the missing
options page — neither manifest declares `options_ui` or `options_page`.

**Where this meets issue #7.** `common.js sendToApp(url, filename)` captures live cookies
(`captureCookies` + `cookieUrlsFor`, including sibling origins) and POSTs them with the URL. It sends
**no referer and no headers**, even though the app has accepted both since v2.5.0. That gap is
harmless-ish for a right-click capture, where the user can retry by hand; it is not harmless for
interception, where we are *replacing* a download the browser was about to perform successfully. The
browser would have sent a referer; if we take the download over and do not, we turn a working
download into a broken one and the user has no idea why. So the extension's context enrichment is
not a nice-to-have alongside interception — it is a precondition, and this change owns it.

**Where this depends on the app.** An intercepted download is added through `/api/add`. The
`issue7-followup-fixes` change fixes the app side of that contract (the GET form dropping context, and
a response that reports what was accepted). This change should land after it, and should use the POST
form, which already models cookies and headers properly.

## Goals / Non-Goals

**Goals:**
- Browser downloads can be taken over by the app, under the user's control.
- An intercepted download carries everything needed to fetch it.
- A failed or impossible hand-off never costs the user the file.
- The extension gains the settings surface it currently lacks.

**Non-Goals:**
- Intercepting anything other than a browser-initiated download — `blob:`/`data:` URLs, streaming
  playback, and extension-internal downloads stay out.
- Firefox parity on day one if its download API differs materially; ship Chromium first and say so
  rather than shipping something half-working on both.
- Any change to the app's UI. The app just receives adds.
- Rebuilding media sniffing, the popup, or the capture flow.

## Decisions

**Intercept via `chrome.downloads`, not `webRequest` blocking.** MV3 removed blocking
`webRequest`, and `declarativeNetRequest` cannot make the "hand this to another program" decision.
`chrome.downloads.onCreated` (with `onDeterminingFilename` where available) fires once the browser has
committed to downloading rather than navigating — which is precisely the event we want, and it hands
us the URL, the referrer and the reported size. `downloads.cancel(id)` then aborts the browser's copy.

**Decide, hand off, and only then cancel.** The order matters more than anything else here. Cancel
first and the user is one failed `fetch` away from having lost the file with no trace. So: evaluate
the rules → send to the app → on a success response, cancel the browser download; on anything else,
leave it running. This makes the "never costs the user the file" requirement a property of the control
flow rather than an aspiration.

**Interception is opt-in.** Existing users updating the extension must not find their browser
behaving differently one morning. Default off, with a first-run prompt in the options page. This also
keeps the store review story simple: the new permission is used only when the user turns the feature
on.

**Rules are pure functions in `common.js`.** `shouldIntercept({url, filename, mime, size, referrer},
settings)` returns a decision, and is unit-tested through `node --test common.test.js` — no browser
needed. The listener in `background.js` stays a thin shell around it. This follows the existing split
(`discoverAppPort`, `cookieUrlsFor`, `mapCookie` are all pure and tested that way).

**Unknown size does not block interception.** `downloads.onCreated` frequently reports
`fileSize: -1`/`0` because the headers have not landed yet. Treating unknown as "too small" would make
interception look broken at random — exactly the class of bug this issue is about. Unknown skips the
size rule and lets the other rules decide.

**The referer comes from the download event, with the tab URL as a fallback.** `DownloadItem.referrer`
is what the browser itself would have sent, so it is the right answer when present; when it is empty
(a direct navigation, some redirect chains) the originating tab's URL is the honest approximation.

**Settings live in `chrome.storage.sync` with a `local` fallback**, alongside the existing `addMode`
key, so a user's rules follow their profile. One versioned settings object, not scattered keys, so
adding a rule later does not need a migration per key.

**Use the POST form of `/api/add` for hand-offs.** It models cookies and headers properly, and it
keeps session cookies out of a URL. The GET form exists for third-party tools that can only build a
URL; we are not one of those.

## Risks / Trade-offs

- **The `downloads` permission is a big ask, and a store-review risk.** "Manage your downloads" reads
  alarmingly in the install prompt, and both stores scrutinise permission increases. Mitigation:
  `PRIVACY.md` and the listing copy state exactly what it is used for, and the feature is off until
  the user enables it. Expect a slower review on the version that adds it, and do not bundle it with
  an urgent fix.
- **Interception fights other download managers.** If the user runs another extension that also
  intercepts, the outcome depends on listener order and is not something we can arbitrate. Worth a
  line in the docs rather than an attempted fix.
- **A cancelled-then-failed download is the worst possible outcome.** The decide-then-cancel ordering
  is the mitigation, and it deserves an explicit test rather than trust — including the case where the
  app answers `201` and then fails to actually start the download.
- **Firefox's download API differs** (`browser.downloads` exists but `onDeterminingFilename` does
  not). Chromium-first, with the Firefox path either behind a capability check or explicitly deferred;
  do not let a shared code path quietly half-work on Firefox.
- **Small files are usually worse in the app** than in the browser — the round trip and the row in the
  list are more overhead than the download. Hence the minimum-size rule, with a default that is a
  judgement call, not a fact.
