## Context

Issue #13 reports that the extension's "Add silently" toggle is ignored on two paths. Both were
verified in the code before this change was written:

| Path | Entry point | Reads the toggle? |
| --- | --- | --- |
| Right-click / popup capture | `sendToApp` — `common.js:330` | **yes** (`getAddMode()`), and `variantId` deliberately overrides it |
| Intercepted browser download | `handOffToApp` — `common.js:484`, called from `background.js:128` | **no** — always `POST /api/add` |
| Third-party API client (Cat Catch) | `LocalApiService.HandleAddAsync` — `LocalApiService.cs:432` | **no** — unconditional `manager.Add(...)` |

The obvious "just call the dialog endpoint" fix does not work, for two reasons already documented in
the code:

1. The dialog entry point is `GET /add?url=…` → `LocalApiService.cs:253` → `MainViewModel.CaptureUrl`,
   which takes a **bare URL string**. A hand-off needs its cookies, referer, User-Agent, mirrors and
   save folder or a protected link dies once the app takes it over (issues #7 and #9).
2. After a hand-off the extension calls `confirmAppFetching(base, id)` and only cancels the browser's
   own download once real bytes or a real size have arrived. That needs the item `id` the dialog
   endpoint never returns. Losing this would either lose the user's file or download it twice.

So the confirmation has to travel over `/api/add` itself, and the app has to keep answering with
something the extension can follow.

## Goals / Non-Goals

**Goals:**
- The extension's silent-vs-dialog toggle governs *every* path the extension hands a link over on.
- A third-party client that cannot be changed (Cat Catch) can still be made to ask, from the app side.
- The Add dialog opens carrying the **whole** hand-off context, not just the URL.
- Interception's "never cost the user the file" guarantee survives intact, including on cancel.
- Nothing changes for anyone who does not opt in: setting off + no `confirm` parameter = today.

**Non-Goals:**
- Changing the legacy `/add?url=` endpoint (it stays exactly as it is, for old extensions).
- Making the CLI add path confirmable — a script must never block on a modal.
- A queue of pending confirmations / a review inbox. One dialog at a time is the whole feature.
- Any change to what is intercepted (`shouldIntercept` and its settings are untouched).

## Decisions

### 1. `confirm` rides on `/api/add`, rather than a new "dialog" endpoint

The request already carries every field the dialog needs to be pre-filled. A second endpoint would
have to re-declare all of them and would drift. `confirm` is parsed by `ApiAddRequest` alongside
`start`, in both the POST body and the GET query, so both existing request forms gain it for free.

*Alternative rejected:* extend the legacy `/add` to take a context. It is the compatibility endpoint
for old extension versions; widening it would put cookies in a query string on the one route that is
deliberately kept dumb.

### 2. A confirm-mode add answers `202` + `ticket`; it never blocks on the user

Holding the HTTP response open until the user answers is the simplest code and the wrong behaviour:
`APP_TIMEOUT_MS.add` would fire long before a user who stepped away comes back, the extension would
read a timeout as failure, and `HandleContextAsync` would sit on a request for minutes. Instead the
app registers a pending confirmation, answers `202 {"ticket": …}` at once, and resolves the ticket
from the dialog's result.

`GET /api/add-status?ticket=…` reports `pending` / `added` (+ `id`) / `cancelled`. The extension already
polls (`confirmAppFetching`), so following a ticket reuses a pattern that exists rather than adding a
new mechanism. Tickets are bounded: an unanswered dialog expires, and an expired or unknown ticket is
`404` — it must never read as `added`.

*Alternative rejected:* a WebSocket / long-poll push. Far more machinery than one boolean warrants.

### 3. The app-wide setting is what covers Cat Catch

A third-party client will never learn to send `confirm`. `DownloadSettings.ConfirmProgrammaticAdds`
(off by default) makes the app treat any `/api/add` without an explicit `confirm` as confirm mode.
The precedence is deliberate and goes both ways: an explicit `confirm: true` asks even with the
setting off, and an explicit `confirm: false` stays silent even with it on — so the extension keeps
full control of its own paths and the setting is a blunt instrument only for clients that have none.

A client that ignores the `202` body still gets what its user wanted: the dialog opens, the user
confirms, the download starts. It just never learns the id. That degradation is the point.

### 4. The dialog is opened by a context-carrying sibling of `CaptureUrl`

`MainViewModel.CaptureUrl(string)` stays as-is for the legacy endpoint. A new path takes the parsed
`ApiAddRequest`, brings the window to front, and opens `AddDownloadItemViewModel` pre-filled from it
(url, filename, path, queue, mirrors, variant) while carrying the non-editable context (cookies,
referer, headers) through to the `DownloadItem` that the confirm builds. `LocalApiService.BuildItem`
already turns an `ApiAddRequest` into a `DownloadItem`; the dialog's job is to let the user amend the
visible fields before that item is handed to `manager.Add`.

Single dialog at a time: while one confirmation is open, a second confirm-mode add gets its own
ticket but is answered as `cancelled` rather than stacking modals — a page that fires several
downloads must not bury the user in windows.

### 5. Extension side: `handOffToApp` gains the mode check, `background.js` gains the wait

`handOffToApp` calls `getAddMode()` and sets `body.confirm = true` in dialog mode. On `202` it polls
`/api/add-status` until `added` (→ carry on into `confirmAppFetching` with the returned id),
`cancelled`, or the wait budget expires (→ `{ ok: false }`, which the interception caller already
reads as "leave the browser's own download alone"). No new failure mode is introduced: every
non-`added` outcome funnels into the existing "the app didn't take it" branch.

`variantId` keeps overriding the toggle here for the same reason it does in `sendToApp`.

## Risks / Trade-offs

- **A page that starts several downloads at once** → only the first opens a dialog; the rest are
  answered `cancelled` and the browser keeps them. Annoying, but never destructive, and far better
  than a stack of modals. Revisit only if reported.
- **The user walks away with the dialog open** → the ticket expires, the extension stops waiting, the
  browser's own download was never cancelled, so the file still arrives. The failure mode is "the app
  didn't take it over", which is the outcome the user already had.
- **A confirm-mode add is slower to reach the wire** → an intercepted download now spends the review
  time downloading in the browser instead of in the app. Acceptable: the user asked to review.
- **Two ways to ask for the same thing** (the request parameter and the app setting) → mitigated by
  one written precedence rule, pinned by tests in both directions.
- **`202` is a new status on a route that only ever answered `201`/`400`** → an old extension never
  sends `confirm`, so it can never see a `202`; only a client that opted in meets the new shape.

## Migration Plan

No data migration. `ConfirmProgrammaticAdds` is absent from existing configs and deserializes to
false, which is today's behaviour. The extension version is bumped and both manifests updated; an
older extension against a newer app, and a newer extension against an older app (no `add-status` →
`404` → treated as "not taken", browser keeps the download), both stay safe.

## Open Questions

- Should the dialog opened by an interception show *which* page the download came from? The referer
  is available and it would help the user judge. Left out of the spec for now — decide during apply.
