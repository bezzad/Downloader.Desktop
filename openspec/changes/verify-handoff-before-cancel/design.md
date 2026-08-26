## Context

The hand-off is deliberately ordered decide → hand off → cancel, and that ordering is the feature's
core safety property. What was missing is that the middle step's success signal is too weak:
`POST /api/add` responds `201` straight after `manager.Add(...)`, and `Start` sets `Status=Running`
synchronously before its first `await`, so the 201 is emitted before a single packet leaves the
machine. Cancelling on it is cancelling on an intention.

The app already exposes everything needed to do better. `GET /api/list` reports per download
`status`, `size`, `downloaded` and `progress`, and `POST /api/add` already returns the new item's
`id`. No desktop-app change is required.

## Goals / Non-Goals

**Goals:**

- Never cancel the browser's download until the app's transfer has demonstrably reached the server.
- On any failure or ambiguity, leave the browser download alone — the user keeps the file.
- Give the app's request the browser's User-Agent, so servers that check it stop refusing it.
- Keep `shouldIntercept` pure and the new logic testable without a browser.

**Non-Goals:**

- Making a single-use link succeed in the app. If the browser already spent the token, the app cannot
  un-spend it; declining gracefully is the correct outcome.
- Changing the desktop app. `/api/list` and the `user-agent` header mapping already exist.
- Removing the existing "Downloading twice" path — a cancel that fails after confirmation is a
  different situation and still needs telling.

## Decisions

**1. Confirmation means "the server answered", not "the app is running".**

Poll `GET /api/list` for the returned `id` and treat the transfer as confirmed when the row reports
`downloaded > 0` **or** `size > 0`. Both require a successful HTTP response — the engine only learns a
total size from a real response — so either is proof the link was fetchable.

`status === "Running"` is deliberately NOT sufficient: the app sets that synchronously before any
network work, so it carries exactly the same weakness as the 201.

A row reporting `Failed` ends the wait immediately: there is nothing to wait for, and the browser
download must be kept.

**2. On timeout, keep the browser's download.**

The wait is bounded (a handful of seconds, polled a few times a second). If nothing is confirmed in
that window the browser download is left running.

*Trade-off, accepted deliberately:* a server with very slow time-to-first-byte may leave the app
downloading as well, so the user gets two copies. That is a visible annoyance; the alternative —
cancelling on an unconfirmed hand-off — is silent data loss. A duplicate file is strictly better than
a lost one, and the user is notified either way.

**3. The confirmation poll lives in `common.js`, not `background.js`.**

`handOffToApp` already owns the app conversation and returns `id`. Putting `confirmAppFetching`
beside it keeps `background.js` a thin listener shell and makes the wait unit-testable against a
stubbed `fetch`, exactly as the existing hand-off tests do.

**4. User-Agent is read once from `navigator.userAgent`.**

Available in both an MV3 service worker and a Firefox background script. It is added to the same
headers map that already carries `Referer`, so it flows through the existing `contextSent` /
`accepted` reporting and the `context-dropped` notification with no new plumbing. The app maps
`user-agent` to `RequestConfiguration.UserAgent` at `DownloadManager.SetHeader`.

## Risks / Trade-offs

- **Interception becomes slower to take effect.** The browser now downloads for a moment longer
  before being cancelled, so a little duplicate traffic is transferred. This is the price of not
  losing files, and is bounded by the confirmation being reached as soon as the first bytes land.
- **Duplicate downloads on a slow server** (Decision 2) — chosen over data loss.
- **Sending the browser's User-Agent makes the app's request more browser-like.** That is the intent;
  it is the same information the browser was about to send to the same server, and it is not
  persisted (it travels with the request context, like cookies).
- **Polling `/api/list` returns every row**, not just ours, so the extension filters by id. Rows are
  small and the poll is short-lived and only runs for an intercepted download.
