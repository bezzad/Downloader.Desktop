## Why

A user who turns OFF the extension's "Add silently" option expects to review every download before
it is added — but the setting is only honoured on the right-click / popup capture path. Downloads the
extension **intercepts** from the browser, and downloads handed over by **third-party API clients**
(Cat Catch and anything else calling `/api/add`), are always added and started silently, with no way
to review or change them first (issue #13).

This is confirmed in the code, not just reported: `handOffToApp` (`common.js`) never calls
`getAddMode()` and always POSTs to `/api/add`, and `LocalApiService.HandleAddAsync` unconditionally
calls `manager.Add(...)`. The app's only dialog entry point is the legacy `/add?url=` endpoint, which
carries a bare URL and therefore cannot be used for a hand-off that must keep its cookies, referer,
headers and mirrors.

## What Changes

- **`/api/add` gains an opt-in `confirm` parameter** (POST body and GET query). When set, the app
  opens the Add dialog **pre-filled with the whole request** — url, filename, save path, mirrors,
  queue, variant, cookies, referer and headers — instead of adding straight away.
- **The request never blocks on the user.** A confirm-mode add answers `202` immediately with a
  `ticket`, and a new **`GET /api/add-status?ticket=…`** reports `pending` / `added` (with the item
  `id`) / `cancelled`. This keeps the extension's existing "wait for real bytes before cancelling the
  browser's copy" safety intact, which a blocking response would break.
- **A new app setting, "Ask before adding programmatic downloads"** (off by default), makes the app
  treat *every* `/api/add` as confirm-mode. This is what covers Cat Catch and other third-party
  clients, which cannot be asked to send a new parameter. Explicit `confirm` in a request wins over
  the setting in both directions.
- **The extension honours its own toggle on both remaining paths**: `handOffToApp` reads the add mode
  and sends `confirm: true` in dialog mode, then resolves the ticket before it touches the browser's
  own download. A user who **cancels** the dialog keeps the browser download — never a lost file.
- **The CLI `--add` payload stays silent unconditionally.** A script must not be able to block on a
  modal, so `MainViewModel.SilentAdd` ignores both the setting and the parameter.

## Capabilities

### New Capabilities

_None — this extends existing capabilities._

### Modified Capabilities

- `local-api`: `/api/add` accepts `confirm` and answers `202` + `ticket` in that mode; new
  `/api/add-status` endpoint; an app setting can force confirm-mode for every programmatic add.
- `browser-extension`: the silent-vs-dialog choice governs **every** hand-off the extension makes,
  not only popup/context-menu captures, and a full-context hand-off can open the dialog.
- `browser-download-interception`: an intercepted download opens the Add dialog when the user is in
  dialog mode, and a cancelled dialog leaves the browser's own download untouched.
- `settings`: new "Ask before adding programmatic downloads" toggle.

## Impact

- App: `Services/LocalApiService.cs` (`HandleAddAsync`, new `add-status` route, pending-ticket
  store), `ViewModels/MainViewModel.cs` (a context-carrying capture path beside `CaptureUrl`),
  `ViewModels/AddDownloadItemViewModel.cs` (pre-fill from an `ApiAddRequest`),
  `Models/DownloadSettings.cs` + `Views/SettingView.axaml` (the new toggle), 16 i18n packs.
- Extension: `common.js` (`handOffToApp`, ticket polling), `background.js` (interception flow).
  Extension version bump; both manifests.
- Unchanged by design: the legacy `/add?url=` endpoint, the CLI add path, and the default behaviour
  of every existing API client (the setting is off and `confirm` is absent → today's silent add).
