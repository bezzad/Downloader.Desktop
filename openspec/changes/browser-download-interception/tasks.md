## 1. Confirm and record the diagnosis

- [ ] 1.1 Confirm on current `develop` that neither manifest declares `downloads` or `options_ui` and
      that no `chrome.downloads` listener exists, so the answer to issue #9 is "never implemented",
      not "broken".
- [ ] 1.2 Draft the reply to issue #9 — state the finding plainly, that his diagnosis was right, and
      that interception is being built. **Show the exact text and get an explicit OK before posting**
      (standing rule); the reply states the request and the current state, not our design.

## 2. Interception rules (pure, testable)

- [ ] 2.1 Define the settings object: `enabled`, `minSizeBytes`, `fileTypes` (mode + list),
      `excludedSites`, with defaults — `enabled: false`.
- [ ] 2.2 Write `shouldIntercept({url, filename, mime, size, referrer}, settings)` in `common.js` as a
      pure function returning a decision plus the reason it decided that way (the reason is what makes
      a user report diagnosable).
- [ ] 2.3 Unit-test it in `common.test.js`: off by default; below/above the minimum; unknown size does
      not block; type in/out of the list; excluded site; `blob:`/`data:`/non-http skipped.

## 3. Hand-off with full context

- [ ] 3.1 Extend `sendToApp` to take and send a referer and headers alongside the cookies it already
      captures, over the POST form of `/api/add`. Context capture stays best-effort — a failure must
      never stop the link being sent.
- [ ] 3.2 Derive the referer from the download event's `referrer`, falling back to the originating
      tab's URL when it is empty.
- [ ] 3.3 Use the app's add response to decide success, including the accepted-context counts that
      `issue7-followup-fixes` adds, so a hand-off that dropped its context is not treated as a win.
- [ ] 3.4 Test the send path with the existing `global.chrome` harness (set before `require`, per the
      established pattern): referer present, referer absent, cookie capture throwing.

## 4. The interception listener

- [ ] 4.1 Add the `downloads` permission to `manifest.json` and `manifest.firefox.json`.
- [ ] 4.2 Add a `downloads.onCreated` listener in `background.js` that consults `shouldIntercept`,
      hands off, and **only cancels the browser download once the hand-off has succeeded**.
- [ ] 4.3 Handle the app being unreachable and the hand-off failing: leave the browser download alone,
      and do not report a takeover that did not happen.
- [ ] 4.4 Make a takeover visible to the user (notification or badge) so a download that vanishes from
      the browser is never unexplained.
- [ ] 4.5 Decide the Firefox path — capability-check `onDeterminingFilename` and either support or
      explicitly skip it there; do not let a shared path half-work.

## 5. Options page

- [ ] 5.1 Add `options.html`/`options.js`/`options.css` and declare `options_ui` in both manifests,
      styled to match `popup.css`.
- [ ] 5.2 Bind every interception setting, persisting to `chrome.storage.sync` with a `local`
      fallback, and have changes take effect on the next download with no reload.
- [ ] 5.3 Link to the options page from the popup.
- [ ] 5.4 Show a first-run explanation of what interception does and what the permission is for, so
      turning it on is an informed choice.

## 6. End-to-end verification

- [ ] 6.1 Add a Playwright spec in `e2e/` covering an intercepted download and a left-alone download
      against a local fixture server (run the suite with `--workers=1` — the shared persistent-context
      Chromium makes parallel runs flaky).
- [ ] 6.2 Test the worst case explicitly: the app accepts the add and then fails to start it — the user
      must not be left with neither a browser download nor a working one.
- [ ] 6.3 Run `node --test src/browser-extension/common.test.js` and the Playwright suite green.
- [ ] 6.4 **Author's manual check**: load the unpacked extension in Chrome, download a real file from a
      real site with interception on, then with it off, then with the app closed.

## 7. Docs and release

- [ ] 7.1 Update `PRIVACY.md` for the `downloads` permission — what it reads, what leaves the machine
      (nothing but the hand-off to loopback), and that it is unused while the feature is off.
- [ ] 7.2 Update the extension `README.md` and the store listing copy in `PUBLISHING.md`, including a
      note that running another intercepting download manager alongside is unsupported.
- [ ] 7.3 Bump the extension version.
- [ ] 7.4 Flag to the author that the permission increase means a slower store review, so this should
      not ride along with an urgent fix.
