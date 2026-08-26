## 1. Type resolution in `common.js`

- [ ] 1.1 Add a content-disposition filename parser that reads `response-content-disposition` and
      `rscd` from a URL's query string and returns the bare filename, tolerating percent-encoding,
      quoted values, `filename*=UTF-8''…` and junk input (never throws).
- [ ] 1.2 Add a minimal MIME→extension map covering only unambiguous types already present in
      `INTERCEPT_FILE_TYPES`; generic containers (`application/octet-stream`, `binary/octet-stream`)
      map to nothing.
- [ ] 1.3 Replace the `extOfName(item.filename) || extOf(url)` type resolution in `shouldIntercept`
      with the ordered chain: suggested filename → content-disposition in the query → URL path →
      MIME. Keep the function pure.
- [ ] 1.4 Keep `reason` precise: `type-unknown` only when no source identified a type;
      `type-not-allowed` / `type-denied` otherwise.
- [ ] 1.5 Add `xapk`, `apks`, `obb` to `INTERCEPT_FILE_TYPES`.
- [ ] 1.6 Export any new helpers from the `module.exports` block so they are unit-testable.

## 2. Listener wiring in `background.js`

- [ ] 2.1 Register `downloads.onDeterminingFilename` where available (Chromium) and pass its
      `suggestedFilename` into the same decision path; keep `downloads.onCreated` for Firefox.
- [ ] 2.2 Ensure a download is considered exactly once when both events fire for it.
- [ ] 2.3 Keep the handler cheap and non-blocking on the non-intercept path so Chromium's filename
      determination is never delayed.
- [ ] 2.4 Preserve the decide → hand off → cancel ordering and every existing failure path unchanged;
      keep registration inside the existing try/catch so a missing event never throws on load.

## 3. Tests

- [ ] 3.1 Unit tests for the content-disposition parser: `rscd`, `response-content-disposition`,
      percent-encoded, quoted, `filename*`, absent, and malformed input.
- [ ] 3.2 Unit tests for the MIME map, including that generic octet-stream identifies nothing.
- [ ] 3.3 Unit tests for `shouldIntercept` on a real GitHub-shaped signed URL (no path extension,
      name only in `rscd`) asserting it now intercepts, plus source-precedence and the
      still-unidentifiable case.
- [ ] 3.4 Unit tests that `xapk` is intercepted by default.
- [ ] 3.5 An e2e test serving a download from an extensionless path with a `Content-Disposition`
      header, asserting it is handed to the app and the browser's copy cancelled.
- [ ] 3.6 Confirm the existing e2e safety tests still pass unchanged (hand-off failure leaves the
      browser download alone).

## 4. Release

- [ ] 4.1 Bump the extension version in `manifest.json` and `manifest.firefox.json`.
- [ ] 4.2 Run all three suites green: `node --test src/browser-extension/common.test.js`, the
      Playwright e2e suite, and `dotnet test` for the app.
- [ ] 4.3 Verify `./scripts/build-extension.sh` still passes its `verify_zip` guard.
- [ ] 4.4 Commit on `develop` referencing issue #9.
