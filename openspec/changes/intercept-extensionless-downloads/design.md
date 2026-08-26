## Context

`shouldIntercept` is a pure function (no browser APIs, no network) so the whole rule set stays
unit-testable and the listener in `background.js` is a thin shell around it. That property is worth
preserving — the fix must not drag browser API calls into the decision.

The current listener wiring is the constraint that caused the bug. `background.js:183-192` drives
Chromium and Firefox from the *same* `downloads.onCreated` event on purpose, and explicitly declines
`onDeterminingFilename` because it is Chromium-only. The cost of that symmetry was not understood at
the time: in Chromium `DownloadItem.filename` is empty at `onCreated`, so the symmetric design threw
away the one field that names the file.

The safety ordering — decide → hand off → cancel the browser's copy only after the app accepts — is
the feature's core guarantee (`Interception never costs the user the file`). Any change to the event
wiring has to keep it intact.

## Goals / Non-Goals

**Goals:**

- A download is judged by what the file is, not by whether its URL path happens to end in `.zip`.
- `shouldIntercept` stays pure and fully unit-testable.
- The decision keeps reporting a precise `reason`; "could not determine the type" stays
  distinguishable from "the user does not want this type".
- Test coverage that would have caught this: a signed, extensionless URL whose name lives only in
  content-disposition.

**Non-Goals:**

- The Softpedia "Secure Download" failure (item 2b): interception fires correctly there and the
  app's re-fetch is rejected. Investigated separately.
- Changing defaults, the settings schema, or the off-by-default switch.
- Intercepting non-HTTP downloads (`blob:`, `data:`) — still impossible to hand off and still
  excluded.

## Decisions

**1. Resolve the type from an ordered list of sources, most trustworthy first.**

`shouldIntercept` gains a single helper that returns the first identifiable type from:

1. the browser's suggested filename (`item.filename`),
2. the filename in the URL's content-disposition query parameters (`response-content-disposition`,
   and GitHub's short `rscd`),
3. the URL path extension (today's only real source),
4. the reported MIME type, mapped to an extension.

Ordering matters: content-disposition is what the browser itself will name the file, so it beats the
path. MIME is last because `application/octet-stream` — which GitHub returns — identifies nothing,
so it must never override a name that does.

*Alternative rejected:* parse only the query string. It fixes GitHub and APKPure but leaves any CDN
that signs without a content-disposition parameter broken, and does nothing about the empty
`filename` in Chromium, which is the deeper defect.

**2. Keep `onCreated` for both browsers. `onDeterminingFilename` was tried and rejected on evidence.**

The plan was to switch Chromium to `onDeterminingFilename`, the only event that carries the browser's
suggested name. It was implemented, and then abandoned after measurement:

- A probe confirmed the premise — `DownloadItem.filename` really is `(empty)` at `onCreated` in
  Chromium, while `mime` is populated.
- But Chromium permits only **one** `onDeterminingFilename` listener per extension (a second
  `addListener` throws "Too many listeners"), making it a scarce, un-shareable slot.
- And it does **not fire at all** when the browser's download behaviour has been set over CDP —
  precisely what an automated browser does. Switching to it turned three previously-green e2e
  interception tests red, including ones unrelated to this change, because the extension simply
  stopped seeing downloads.

Shipping a primary code path that cannot be exercised by our own suite is a worse trade than the gap
it closes — especially since Decision 1 already recovers the type for every case in the report.
`onCreated` stays the single event for both browsers.

*Residual gap, accepted and documented in the code:* a download named **only** by the browser's
suggestion — no extension in the URL path, no content-disposition anywhere, and an unidentifiable
MIME — is still left to the browser. None of the reported sites are in that category.

**3. Keep MIME mapping deliberately small.**

Only unambiguous types that map to an extension already in `INTERCEPT_FILE_TYPES` (e.g.
`application/vnd.android.package-archive` → `apk`, `application/x-msdownload` → `exe`,
`application/zip` → `zip`). Generic containers (`application/octet-stream`,
`binary/octet-stream`) map to nothing. A large speculative table would start intercepting things
the user never asked for, which is worse than missing one.

**4. Add `xapk`, `apks`, `obb` to the default type list.**

`xapk` is absent entirely, so APKPure could never have matched even after the path fix. `apks` and
`obb` are the same Android-distribution family and are equally clearly "a file you download", not
something a page needs.

## Risks / Trade-offs

- **More downloads now get intercepted.** That is the point, but it means a user who enabled
  interception before the fix will see it act on links it previously ignored. Since the feature is
  off by default and the type list is user-visible, this is the behaviour they asked for; it needs
  no migration but does belong in the release notes.
- **`onDeterminingFilename` fires for every download in Chromium**, including ones we ignore. The
  handler must stay cheap for the non-intercept path and must not delay the browser.
- **Content-disposition parsing is a small parser on hostile input.** It must never throw — an
  unparseable value falls through to the next source rather than failing the decision.
- **MIME mapping can drift** as sites serve sloppy types. Keeping the table minimal bounds the
  damage; the `reason` field keeps a wrong decision diagnosable from a user report.
