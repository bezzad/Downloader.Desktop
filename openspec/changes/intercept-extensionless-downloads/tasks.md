## 1. Type resolution in `common.js`

- [x] 1.1 Add a content-disposition filename parser that reads `response-content-disposition` and
      `rscd` from a URL's query string and returns the bare filename, tolerating percent-encoding,
      quoted values, `filename*=UTF-8''…` and junk input (never throws).
- [x] 1.2 Add a minimal MIME→extension map covering only unambiguous types already present in
      `INTERCEPT_FILE_TYPES`; generic containers (`application/octet-stream`, `binary/octet-stream`)
      map to nothing.
- [x] 1.3 Replace the `extOfName(item.filename) || extOf(url)` type resolution in `shouldIntercept`
      with the ordered chain: suggested filename → content-disposition in the query → URL path →
      MIME. Keep the function pure.
- [x] 1.4 Keep `reason` precise: `type-unknown` only when no source identified a type;
      `type-not-allowed` / `type-denied` otherwise.
- [x] 1.5 Add `xapk`, `apks`, `obb` to `INTERCEPT_FILE_TYPES`.
- [x] 1.6 Export any new helpers from the `module.exports` block so they are unit-testable.

## 2. Listener wiring in `background.js`

- [x] 2.1 Evaluate `downloads.onDeterminingFilename` for Chromium. **Implemented, then reverted on
      evidence**: Chromium allows only one such listener per extension, and it never fires when the
      download behaviour is set over CDP, which turned three unrelated e2e tests red. `onCreated`
      remains the single event for both browsers — see design.md, Decision 2.
- [x] 2.2 Not needed — only one event is registered, so a download is considered exactly once.
- [x] 2.3 Not needed — the handler no longer sits in the filename-determination path.
- [x] 2.4 Decide → hand off → cancel ordering and every failure path left unchanged; registration
      still capability-checked so a missing API never throws on load.
- [x] 2.5 Record the residual gap in code: a download named ONLY by the browser's suggestion (no path
      extension, no content-disposition, unidentifiable MIME) is still left to the browser.

## 3. Tests

- [x] 3.1 Unit tests for the content-disposition parser: `rscd`, `response-content-disposition`,
      percent-encoded, quoted, `filename*`, absent, and malformed input.
- [x] 3.2 Unit tests for the MIME map, including that generic octet-stream identifies nothing.
- [x] 3.3 Unit tests for `shouldIntercept` on a real GitHub-shaped signed URL (no path extension,
      name only in `rscd`) asserting it now intercepts, plus source-precedence and the
      still-unidentifiable case.
- [x] 3.4 Unit tests that `xapk` is intercepted by default.
- [x] 3.5 An e2e test downloading from an extensionless path whose name is carried in the URL's
      `rscd` parameter, asserting it is handed to the app and the browser's copy cancelled. (The name
      had to move from the response HEADER to the query: `onCreated` never sees response headers, so
      a header-only name is the documented residual gap, not something the test can assert.)
- [x] 3.6 Confirm the existing e2e safety tests still pass unchanged (hand-off failure leaves the
      browser download alone).

## 4. Release

- [x] 4.1 Bump the extension version in `manifest.json` and `manifest.firefox.json`.
- [x] 4.2 Run all three suites green: `node --test src/browser-extension/common.test.js`, the
      Playwright e2e suite, and `dotnet test` for the app.
- [x] 4.3 Verify `./scripts/build-extension.sh` still passes its `verify_zip` guard.
- [ ] 4.4 Commit on `develop` referencing issue #9.
