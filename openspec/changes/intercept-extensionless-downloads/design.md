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

**2. Use `onDeterminingFilename` in Chromium, keep `onCreated` for Firefox.**

This reverses the original symmetry decision, which is the root cause. `onDeterminingFilename`
supplies `suggestedFilename` — the real name, before the file is written — and is the event Chromium
provides for exactly this purpose. Firefox has no such event but populates `filename` at
`onCreated`, so each browser is driven by the event that actually carries the name.

Both paths funnel into the same `onDownloadCreated` logic, so the decide → hand off → cancel
ordering and every failure path are shared, not duplicated. The listener registration stays inside
the existing try/catch so a browser lacking either event simply never intercepts rather than
throwing on load.

*Risk accepted:* `onDeterminingFilename` requires the `downloads.shelf`-adjacent behaviour of
returning quickly. The handler must not block on the hand-off — it decides, then does the async work
without holding up the browser's filename determination.

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
