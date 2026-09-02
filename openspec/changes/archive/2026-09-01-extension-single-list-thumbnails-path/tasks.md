## 1. App: settings endpoint

- [x] 1.1 Add a `settings` route to `LocalApiService.HandleApiAsync` answering `GET /api/settings` with
      `{ defaultSavePath, version }` — read-only, no secrets (no cookies/headers/proxy password).
- [x] 1.2 Add a test in `src/Downloader.Desktop.Tests/Integration/` asserting the endpoint returns the
      configured `DefaultSavePath` and that it adds/changes nothing.

## 2. Extension: type ordering replaces the relevance split

- [x] 2.1 Add pure `mediaTypePriority(url)` + `sortDetectedGroups(groups)` to `common.js` (HLS, DASH,
      mp4, other video, audio, other; tie-break size desc then title) and export them for tests.
- [x] 2.2 Delete `computeMainGroups`, `MAIN_WINDOW_MS` and their exports from `common.js`.
- [x] 2.3 `background.js`: drop the `activeHint` map, the `activeMediaHint` message branch, the `main`
      flag from the `getMedia` response, and the hint clean-up in the tab listeners.
- [x] 2.4 Delete `content.js` and its `content_scripts` entry from both manifests.
- [x] 2.5 `popup.js`: one list — remove `mainGroups`/`otherGroups`, render `sortDetectedGroups(...)`
      into a single `<ul>`, and drop the `main` field from `addItem`/`rawItems`.
- [x] 2.6 `popup.html`/`popup.css`: remove the "Main media" heading and the `<details>` "Other
      detected" section; keep the "Detected media" head and the scan/empty states.
- [x] 2.7 Replace the relevance unit tests in `common.test.js` with ordering tests (manifest first,
      size tie-break, unprobed items keep position, unknown extensions last).

## 3. Extension: thumbnails

- [x] 3.1 Add pure `buildThumbnailIndex(shots, pageImage)` + `pickThumbnail(index, group)` to
      `common.js` (exact `currentSrc`/`src`/`groupKey` match first, then the page-level preview).
- [x] 3.2 `popup.js`: collect previews via `api.scripting.executeScript` — per `<video>`/`<audio>`:
      `currentSrc`/`src`, `poster`, and a capped-width canvas JPEG frame (`SecurityError` = a miss);
      plus the page's `og:image`/`twitter:image`. Never throws; never delays first render.
- [x] 3.3 `popup.js`/`popup.css`: fixed-size leading preview slot per row with a type placeholder,
      `<img>` only when a source exists, `onerror` → placeholder, no reflow when previews arrive.
- [x] 3.4 Unit tests for the mapping helpers (exact match wins, page fallback, no sources → null).
- [x] 3.5 Confirm no image data is ever included in a hand-off (assert the add payload shape in a test).

## 4. Extension: download folder

- [x] 4.1 `common.js`: `getSavePath()`/`setSavePath()` over `api.storage.local` and
      `fetchAppDefaultSavePath()` reading `/api/settings` (404 / unreachable → null, never throws).
- [x] 4.2 `common.js`: include `path` in `sendToAppSilently` (JSON body and GET query) and in
      `handOffToApp`, only when a folder is configured; legacy `/add?url=` untouched.
- [x] 4.3 `options.html`/`options.js`: a "Save downloads to" text field prefilled with the saved value
      when set, otherwise the app's default; saves on change like the other fields.
- [x] 4.4 Unit tests: the folder reaches both send forms; no folder configured → parameter absent;
      a saved value is not overwritten by the app's default.

## 5. Release hygiene, tests, docs

- [x] 5.1 Bump both manifests to `1.7.0`.
- [x] 5.2 Update `src/browser-extension/README.md` (single list, previews, download folder) and
      `PRIVACY.md` (frame capture stays local; the `path` sent to the app) — and drop the content
      script from any permission explanation that names it.
- [x] 5.3 Update the Playwright `e2e/` specs that assert the Main/Other split; add one asserting the
      single ordered list.
- [x] 5.4 Run `node --test src/browser-extension/common.test.js` and the e2e suite
      (`npx playwright test --workers=1`) green.
- [x] 5.5 Run `dotnet build Downloader.Desktop.sln -t:Rebuild --nologo` (0 warnings) and the bounded
      `dotnet test` suite green.
- [x] 5.6 Commit and push to `develop`; `/opsx:sync` + `/opsx:archive` the change.
