## 1. App — request parsing and the setting

- [x] 1.1 Add `Confirm` (nullable bool) to `ApiAddRequest`, parsed from the POST JSON body and the GET query in `LocalApiService.cs`, defaulting to unset (not false) so the setting can decide.
- [x] 1.2 Add `ConfirmProgrammaticAdds` (default false) to `Models/DownloadSettings.cs`.
- [x] 1.3 Add a single helper that resolves confirm mode from the request and the setting, with the documented precedence (explicit request value always wins, both ways).
- [x] 1.4 Unit-test the precedence matrix: setting off/on × `confirm` unset/true/false → six expected outcomes.

## 2. App — pending confirmations and the endpoints

- [x] 2.1 Add a pending-confirmation store in `LocalApiService`: ticket → state (`pending`/`added`+id/`cancelled`), with a bounded lifetime and expiry.
- [x] 2.2 Branch `HandleAddAsync`: in confirm mode register a ticket, hand the parsed `ApiAddRequest` to the UI, and respond `202 {"ticket":…}` without waiting for the user; otherwise keep today's `201` path byte-for-byte.
- [x] 2.3 Add the `add-status` route: `200` with `pending`/`added`(+`id`)/`cancelled`, `404` for an unknown or expired ticket, `400` for a missing ticket.
- [x] 2.4 Refuse to stack modals: a confirm-mode add arriving while a confirmation dialog is open resolves its own ticket as `cancelled`.
- [x] 2.5 Tests: `202` shape, ticket lifecycle through all three states, expiry never reads as `added`, unknown ticket is `404`, a confirm-mode add creates no item until confirmed.

## 3. App — the pre-filled dialog

- [x] 3.1 Add a context-carrying capture path on `MainViewModel` (beside `CaptureUrl`, which stays as-is for the legacy `/add` endpoint) that brings the window to front and opens the Add dialog from an `ApiAddRequest`.
- [x] 3.2 Pre-fill `AddDownloadItemViewModel` from the request: url, filename, save path, queue, mirrors, variant — editable — and carry cookies/referer/headers through untouched to the built `DownloadItem`.
- [x] 3.3 Resolve the ticket from the dialog result: confirm → `manager.Add(...)` honouring `start`, then `added` + id; cancel/close → `cancelled`, nothing added.
- [x] 3.4 Keep `SilentAdd` (the CLI payload path) unconditionally silent, and pin it with a test that turns the setting on.
- [x] 3.5 Headless UI tests: a confirm-mode add opens a pre-filled dialog carrying every field; confirming creates the item **with its cookies/headers/referer intact**; cancelling creates nothing.

## 4. App — settings UI and wording

- [x] 4.1 Add the "Ask before adding programmatic downloads" toggle to `Views/SettingView.axaml` next to the browser-integration options, bound to `ConfirmProgrammaticAdds`.
- [x] 4.2 Add the label and its help text to all 16 i18n packs in `Assets/i18n/`, describing it in user terms (extension and other local-API tools; not the app's own Add dialog or the CLI).
- [x] 4.3 Test that the setting round-trips through config save/load.

## 5. Extension — honour the toggle on the hand-off path

- [x] 5.1 In `handOffToApp` (`common.js`), read `getAddMode()` and set `body.confirm = true` in dialog mode; keep an explicit `variantId` silent, matching `sendToApp`.
- [x] 5.2 Add a ticket-following helper (poll `/api/add-status` to `added`/`cancelled`/budget-expired), built like `confirmAppFetching`: injectable `now`/`sleep`, never throws.
- [x] 5.3 On `202`, resolve the ticket and return the item id on `added`; return `{ ok: false }` with a distinct reason for `cancelled` and for the expired budget, so the interception caller falls into its existing "the app didn't take it" branch.
- [x] 5.4 Node tests (`common.test.js`): dialog mode sends `confirm`; silent mode does not; a hand-off keeps its cookies/referer/headers/mirrors/path in dialog mode; `cancelled` and a hung ticket are both failed hand-offs; a `404` on `add-status` (older app) is a failed hand-off, never a success.

## 6. Extension — interception flow and release plumbing

- [x] 6.1 Verify `background.js`'s `onDownloadCreated` needs no change beyond the new `result.ok` semantics, and that a cancelled dialog leaves `cancelBrowserDownload` uncalled.
- [x] 6.2 Bump the extension version in `manifest.json` and `manifest.firefox.json`.
- [x] 6.3 Playwright e2e: with the toggle set to "Open dialog", an intercepted download reaches a stubbed app as a `confirm` request and the browser's own download survives a cancellation.

## 7. Documentation

- [x] 7.1 Document `confirm`, the `202`+`ticket` shape and `/api/add-status` in the local-API docs (`src/browser-extension/README.md` and wherever the API is described for third-party clients such as Cat Catch).
- [x] 7.2 Note the new setting in `CLAUDE.md`'s roadmap entry for this change.

## 8. Verification

- [x] 8.1 `dotnet build Downloader.Desktop.sln -t:Rebuild --nologo` → 0 errors, **0 warnings**.
- [x] 8.2 `dotnet test -v q --nologo` green; `node --test src/browser-extension/common.test.js` green; the Playwright suite in `src/browser-extension/e2e/` green.
- [x] 8.3 Manual check against the issue: with the extension toggle off, an intercepted download and a raw `curl` to `/api/add` (setting on) both open the dialog; with it on/off respectively, both stay silent.

  **Run against a real app on a real desktop, 2026-09-05** (`dotnet run` with an isolated
  `XDG_CONFIG_HOME` so the developer's own config was never touched — verified unmodified afterwards).
  The dialog was confirmed to be a real window each time via `xwininfo -root -tree`, not merely inferred
  from the API:

  | Case | Result |
  |---|---|
  | setting off, `"confirm":true` | `202` + ticket, `"Add download"` 560×560 window on screen, `/api/list` **empty**, ticket `pending` |
  | second confirm-mode add while that dialog was open | own ticket `cancelled`, still exactly **one** dialog window (no stacking) |
  | setting **on**, plain add with no `confirm` (the Cat Catch case) | `202` + ticket, dialog opened, nothing added |
  | setting **on**, `"confirm":false` | `201`, added silently — the opt-out wins over the setting |
  | setting off, plain add | `201`, added silently — unchanged for existing clients |
  | `GET /api/add?…&confirm=true` (query form) | `202` |
  | `/api/add-status` unknown ticket / no ticket | `404` / `400` |

  The setting itself round-tripped through a real restart (written to `config.json`, read back, and it
  changed the behaviour). Note the first window poll after the `202` found no dialog yet — that is the
  "answers without waiting for the user" property showing up in practice.

  **Not covered by this run:** clicking Download/Cancel in the dialog, and eyeballing the pre-filled
  fields — GNOME refused a screenshot to a non-interactive client (`org.gnome.Shell.Screenshot`:
  *Screenshot is not allowed*) and no input-injection tool (`xdotool`/`ydotool`) is installed. Both are
  covered by `UI/ConfirmAddDialogTests`, which opens the real dialog, asserts the pre-filled url/name/
  folder, and asserts that confirming creates the item with its cookies/headers/referer intact while
  cancelling creates nothing. The intercepted-download half is covered by the two Playwright cases in
  `e2e/tests/interception.spec.js`, which drive a real `chrome.downloads` event through the real
  extension in a real Chromium.
- [x] 8.4 Regenerate `docs/screenshots/` for the Settings page (a control was added) and eyeball the PNGs before committing.
