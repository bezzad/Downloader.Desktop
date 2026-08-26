## 1. User-Agent in the hand-off (fix A)

- [x] 1.1 Add the browser's `navigator.userAgent` to the headers `handOffToApp` sends, alongside the
      existing `Referer`, guarding for a context where `navigator` is absent.
- [x] 1.2 Confirm it flows through `contextSent` / `accepted` and the `context-dropped` reporting
      without new plumbing.

## 2. Confirm before cancelling (fix B)

- [x] 2.1 Add `confirmAppFetching(port, id, opts)` to `common.js`: polls `GET /api/list`, finds the
      row by id, resolves confirmed when `downloaded > 0` or `size > 0`, resolves failed when the row
      reports `Failed`, resolves timed-out when the bounded wait elapses. Never throws.
- [x] 2.2 Have `onDownloadCreated` cancel the browser's download only after a confirmed result.
- [x] 2.3 On failed or timed-out, leave the browser download running and notify the user that the app
      did not take it.
- [x] 2.4 Keep the existing "Downloading twice" path for a cancel that fails after confirmation.
- [x] 2.5 Export the new helper for unit testing.

## 3. Tests

- [x] 3.1 Unit: `confirmAppFetching` confirms on `downloaded > 0`, confirms on `size > 0`, and does
      NOT confirm on `status: "Running"` alone.
- [x] 3.2 Unit: it resolves failed as soon as the row reports `Failed`, without waiting out the
      timeout.
- [x] 3.3 Unit: it resolves timed-out when the row never progresses, and never throws on a bad or
      unreachable `/api/list`.
- [x] 3.4 Unit: the hand-off carries a `User-Agent` header.
- [x] 3.5 e2e: a stub app that accepts the add but never reports progress leaves the browser's
      download running (the Softpedia shape) — the regression test for the data-loss bug.
- [x] 3.6 e2e: a stub app that accepts and reports progress still cancels the browser's copy, so the
      existing happy path is unchanged.

## 4. Release

- [x] 4.1 Bump the extension version in `manifest.json` and `manifest.firefox.json`.
- [x] 4.2 All three suites green: extension unit, Playwright e2e, and `dotnet test`.
- [x] 4.3 `./scripts/build-extension.sh` still passes its `verify_zip` guard; solution rebuild has 0
      warnings.
- [x] 4.4 Commit on `develop` referencing issue #9.
