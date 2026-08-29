## Why

Interception can lose the user's file outright — the one thing this feature promises never to do.

`POST /api/add` calls `manager.Add(...)` and returns `201` **immediately**, before any network
contact (`Start` sets `Status=Running` synchronously, before its first `await`). The extension treats
that 201 as acceptance and cancels the browser's own download. If the app's fetch then fails, the
user is left with **nothing**: the browser copy is gone and the app's never started.

The spec requirement `Interception never costs the user the file` and the ordering comment in
`background.js` both claim the cancel happens "only once the app has accepted it". In practice
"accepted" means "queued", not "reachable" — the guarantee is not actually implemented.

@ray2me123 hit this on Softpedia's "Secure Download" (issue #9): the download was intercepted and
then always failed with "This link is no longer valid". Two mechanisms fit his evidence and cannot be
separated without reaching the site (Cloudflare blocks our CI/dev IP):

1. **A one-time or short-lived token.** At `onCreated` the browser's request is already `in_progress`
   (verified by probe), so the app's request is the second to present the token and loses.
2. **User-Agent binding.** `DownloadSettings.UserAgent` defaults to null and the extension sends only
   `Referer`, so the app's request looks nothing like the browser's.

Ruled out: IP binding (same machine) and cookies/referer — both are already sent, and Softpedia's
*external mirror* downloads fine with exactly that context.

Whichever it is, the user should not lose the file over it.

## What Changes

- **Send the browser's User-Agent with the hand-off.** The extension adds `User-Agent` to the headers
  it already sends. The app needs no change: `DownloadManager.SetHeader` already routes `user-agent`
  to `RequestConfiguration.UserAgent`. This alone may fix Softpedia if mechanism 2 is the cause.
- **Verify before cancelling.** After a successful add, the extension confirms the app's download has
  actually reached the server — the app reports a known size or received bytes — before cancelling
  the browser's copy. If the app reports a failure, or nothing is confirmed within a bounded wait,
  the browser's download is left alone and the user is told.

Not in scope: making a genuinely one-time link succeed in the app. The app cannot spend a token the
browser already spent; the honest outcome is that interception declines and the browser keeps the
file.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `browser-download-interception`: the requirement that interception never costs the user the file
  gains the condition that the browser's download is cancelled only once the app's transfer is
  **confirmed to have reached the server**, not merely accepted for queueing; and an intercepted
  download's request context includes the browser's User-Agent.

## Impact

- `src/browser-extension/common.js` — `handOffToApp` (User-Agent in the context), plus a new bounded
  confirmation poll against `/api/list`.
- `src/browser-extension/background.js` — `onDownloadCreated` cancels only after confirmation, and
  reports the "browser kept it" outcome.
- `src/browser-extension/common.test.js`, `src/browser-extension/e2e/` — coverage for confirmed,
  failed and timed-out hand-offs.
- Extension version bump.
- No desktop-app (C#) change: `/api/list` and the `user-agent` header mapping already exist.
