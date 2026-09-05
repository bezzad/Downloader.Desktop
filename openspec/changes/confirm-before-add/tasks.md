## 1. App — request parsing and the setting

- [ ] 1.1 Add `Confirm` (nullable bool) to `ApiAddRequest`, parsed from the POST JSON body and the GET query in `LocalApiService.cs`, defaulting to unset (not false) so the setting can decide.
- [ ] 1.2 Add `ConfirmProgrammaticAdds` (default false) to `Models/DownloadSettings.cs`.
- [ ] 1.3 Add a single helper that resolves confirm mode from the request and the setting, with the documented precedence (explicit request value always wins, both ways).
- [ ] 1.4 Unit-test the precedence matrix: setting off/on × `confirm` unset/true/false → six expected outcomes.

## 2. App — pending confirmations and the endpoints

- [ ] 2.1 Add a pending-confirmation store in `LocalApiService`: ticket → state (`pending`/`added`+id/`cancelled`), with a bounded lifetime and expiry.
- [ ] 2.2 Branch `HandleAddAsync`: in confirm mode register a ticket, hand the parsed `ApiAddRequest` to the UI, and respond `202 {"ticket":…}` without waiting for the user; otherwise keep today's `201` path byte-for-byte.
- [ ] 2.3 Add the `add-status` route: `200` with `pending`/`added`(+`id`)/`cancelled`, `404` for an unknown or expired ticket, `400` for a missing ticket.
- [ ] 2.4 Refuse to stack modals: a confirm-mode add arriving while a confirmation dialog is open resolves its own ticket as `cancelled`.
- [ ] 2.5 Tests: `202` shape, ticket lifecycle through all three states, expiry never reads as `added`, unknown ticket is `404`, a confirm-mode add creates no item until confirmed.

## 3. App — the pre-filled dialog

- [ ] 3.1 Add a context-carrying capture path on `MainViewModel` (beside `CaptureUrl`, which stays as-is for the legacy `/add` endpoint) that brings the window to front and opens the Add dialog from an `ApiAddRequest`.
- [ ] 3.2 Pre-fill `AddDownloadItemViewModel` from the request: url, filename, save path, queue, mirrors, variant — editable — and carry cookies/referer/headers through untouched to the built `DownloadItem`.
- [ ] 3.3 Resolve the ticket from the dialog result: confirm → `manager.Add(...)` honouring `start`, then `added` + id; cancel/close → `cancelled`, nothing added.
- [ ] 3.4 Keep `SilentAdd` (the CLI payload path) unconditionally silent, and pin it with a test that turns the setting on.
- [ ] 3.5 Headless UI tests: a confirm-mode add opens a pre-filled dialog carrying every field; confirming creates the item **with its cookies/headers/referer intact**; cancelling creates nothing.

## 4. App — settings UI and wording

- [ ] 4.1 Add the "Ask before adding programmatic downloads" toggle to `Views/SettingView.axaml` next to the browser-integration options, bound to `ConfirmProgrammaticAdds`.
- [ ] 4.2 Add the label and its help text to all 16 i18n packs in `Assets/i18n/`, describing it in user terms (extension and other local-API tools; not the app's own Add dialog or the CLI).
- [ ] 4.3 Test that the setting round-trips through config save/load.

## 5. Extension — honour the toggle on the hand-off path

- [ ] 5.1 In `handOffToApp` (`common.js`), read `getAddMode()` and set `body.confirm = true` in dialog mode; keep an explicit `variantId` silent, matching `sendToApp`.
- [ ] 5.2 Add a ticket-following helper (poll `/api/add-status` to `added`/`cancelled`/budget-expired), built like `confirmAppFetching`: injectable `now`/`sleep`, never throws.
- [ ] 5.3 On `202`, resolve the ticket and return the item id on `added`; return `{ ok: false }` with a distinct reason for `cancelled` and for the expired budget, so the interception caller falls into its existing "the app didn't take it" branch.
- [ ] 5.4 Node tests (`common.test.js`): dialog mode sends `confirm`; silent mode does not; a hand-off keeps its cookies/referer/headers/mirrors/path in dialog mode; `cancelled` and a hung ticket are both failed hand-offs; a `404` on `add-status` (older app) is a failed hand-off, never a success.

## 6. Extension — interception flow and release plumbing

- [ ] 6.1 Verify `background.js`'s `onDownloadCreated` needs no change beyond the new `result.ok` semantics, and that a cancelled dialog leaves `cancelBrowserDownload` uncalled.
- [ ] 6.2 Bump the extension version in `manifest.json` and `manifest.firefox.json`.
- [ ] 6.3 Playwright e2e: with the toggle set to "Open dialog", an intercepted download reaches a stubbed app as a `confirm` request and the browser's own download survives a cancellation.

## 7. Documentation

- [ ] 7.1 Document `confirm`, the `202`+`ticket` shape and `/api/add-status` in the local-API docs (`src/browser-extension/README.md` and wherever the API is described for third-party clients such as Cat Catch).
- [ ] 7.2 Note the new setting in `CLAUDE.md`'s roadmap entry for this change.

## 8. Verification

- [ ] 8.1 `dotnet build Downloader.Desktop.sln -t:Rebuild --nologo` → 0 errors, **0 warnings**.
- [ ] 8.2 `dotnet test -v q --nologo` green; `node --test src/browser-extension/common.test.js` green; the Playwright suite in `src/browser-extension/e2e/` green.
- [ ] 8.3 Manual check against the issue: with the extension toggle off, an intercepted download and a raw `curl` to `/api/add` (setting on) both open the dialog; with it on/off respectively, both stay silent.
- [ ] 8.4 Regenerate `docs/screenshots/` for the Settings page (a control was added) and eyeball the PNGs before committing.
