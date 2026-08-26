## Why

Interception silently does nothing on most real download buttons. @ray2me123, testing v2.6.1 on
issue #9, found GitHub Releases (APK/ZIP/EXE), APKPure (APK/XAPK) and Softpedia ZIP downloads are
never taken over, while FilePuma, FileHippo, AdGuard, Archive.org, Quetta and APKMirror work.

The dividing line is not the site — it is whether the file extension happens to appear in the URL
**path**. `shouldIntercept` derives the type from `extOfName(item.filename) || extOf(url)`, and:

- `extOf` (`common.js:81`) parses `pathname` only and ignores the query string entirely;
- in Chromium `DownloadItem.filename` is empty at `onCreated`, and `background.js` deliberately does
  not use `onDeterminingFilename` (the only event that knows the suggested name) so that both
  browsers run off one event;
- `item.mime` is passed into `shouldIntercept` and never read.

So every signed CDN link resolves to `ext === ""` → `type-unknown` → no interception. Verified
against a real GitHub asset, whose final URL path is
`/github-production-release-asset/830513186/76697026-…` with the filename present only in the
`rscd` / `response-content-disposition` query parameters. `xapk` is additionally absent from the
default type list.

Every e2e interception test fetches `/sample.zip?…`, so the extensionless case — the common case in
the wild — has no coverage at all.

## What Changes

- Resolve the download's file type from an ordered chain of sources instead of the URL path alone:
  the browser's suggested filename (Firefox supplies one at creation) → the filename carried in the
  URL's `response-content-disposition` / `rscd` query parameters → the URL path → the MIME type.
- Keep `downloads.onCreated` as the single event for both browsers. Switching Chromium to
  `onDeterminingFilename` was implemented and then reverted on evidence — see design.md, Decision 2.
- Let `item.mime` participate in the type decision as a last resort, so an `application/vnd.android.package-archive`
  with no usable name is still recognised.
- Add `xapk` (and the related `apks`, `obb`) to `INTERCEPT_FILE_TYPES`.
- Cover the extensionless signed-CDN shape in unit and e2e tests.

Not in scope: the Softpedia "Secure Download" failure (item 2b of the report), where interception
*does* fire and the app's re-fetch is rejected as an expired link. That is a different defect — a
probably single-use token — and is being investigated separately so it does not hold up this fix.

**No breaking change**: the settings shape, defaults and the off-by-default switch are untouched.
A user's existing rules keep their meaning; strictly more downloads now match them.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `browser-download-interception`: the requirement governing what is intercepted currently constrains
  only the user's rules. It gains the behaviour that the **file type is resolved from the browser's
  suggested filename and response metadata, not solely the URL path**, so a signed extensionless link
  is judged by what the file actually is.

## Impact

- `src/browser-extension/common.js` — `extOf`/`extOfName` callers, `shouldIntercept` type resolution,
  `INTERCEPT_FILE_TYPES`.
- `src/browser-extension/background.js` — the listener wiring: a Chromium `onDeterminingFilename`
  path alongside the existing `onCreated` path, without weakening the decide → hand off → cancel
  ordering that keeps a failed hand-off from costing the user the file.
- `src/browser-extension/common.test.js` and `src/browser-extension/e2e/` — new coverage.
- Extension version bump in `manifest.json` + `manifest.firefox.json`.
- No desktop-app (C#) code is affected.
