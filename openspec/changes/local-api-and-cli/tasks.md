# Tasks — local-api-and-cli

## 1. Local API service

- [x] 1.1 Rename `BrowserIntegrationService` → `LocalApiService` (same port 15151, same toggle/start/stop wiring; legacy `/add?url=` + `/ping` code paths byte-for-byte unchanged, incl. CORS header)
- [x] 1.2 Add pure request-model helpers: `ApiAddRequest` parse (JSON body + query form; url validation, mirrors, start default true, 64 KB body cap) and JSON response shapes for add/list/control — unit-testable with no networking
- [x] 1.3 Implement `/api/add`: build a `DownloadItem` (urls, filename?, folder ?? settings default, queue by name/id ?? default queue), `Dispatcher.UIThread.InvokeAsync` → `manager.Add(item, autoStart)`, respond 201 with id/name/status (400 on invalid input)
- [x] 1.4 Implement `GET /api/list`: UI-thread snapshot of `manager.Items` → JSON array (id, name, url, status, progress, size, downloaded, speed, folder, filePath, queue)
- [x] 1.5 Implement `/api/pause|resume|cancel|retry|remove` by id: find vm by `Item.Id`, dispatch the manager call, 200 ok / 404 unknown id; no CORS headers on any `/api/*` response

## 2. CLI verbs

- [x] 2.1 Pure `CliParser.TryParse(args)`: verbs `add/list/pause/resume/cancel/retry/remove`, add options (`--url/--filename/--path/--queue/--no-start`), usage text, exit-code contract (0/1/2); bare-URL and `--minimized` launches fall through to the GUI path
- [x] 2.2 Wire CLI mode in `Program.Main` before Avalonia; Windows `AttachConsole(ATTACH_PARENT_PROCESS)` P/Invoke for visible output
- [x] 2.3 `add` delivery: running instance → forward `add:{json}` over the single-instance channel (15152) and exit 0; not running → spawn self detached with `--cli-add <json>` and exit 0
- [x] 2.4 Extend `SingleInstanceService`/`MainViewModel` message handling: `add:{json}` → silent `DownloadManager.Add`; plain URL line keeps opening the Add dialog (today's behavior); handle `--cli-add` at GUI startup
- [x] 2.5 `list` + control verbs: HTTP client against `/api/*`; JSON to stdout for `list`; friendly one-line error + exit 1 when the app isn't running or the toggle is off

## 3. Settings, default-on & i18n

- [x] 3.1 Flip the integration toggle default to **on** in `DownloadSettings`; add a one-time migration (config schema-version/hook) so existing configs are enabled once on load without clobbering a later user choice
- [x] 3.2 Update the Settings toggle label + add a short in-app description covering the local API (persisted property name unchanged for config compat); add the new i18n keys to `en.json` (fallback covers other packs)

## 4. Browser extension

- [x] 4.1 `common.js`: add `sendToAppSilently(url, filename?)` → `/api/add`, with graceful fallback to `/add?url=` on 404/unavailable
- [x] 4.2 `popup.html`/`popup.js`/`popup.css`: "Add silently / Open dialog" toggle persisted in `api.storage` (default silent); keep the `/ping` reachability dot; forward suggested filename when available
- [x] 4.3 Bump `manifest.json` + `manifest.firefox.json` version; refresh extension `README.md` / `PRIVACY.md` / `PUBLISHING.md` for the new behavior

## 5. Tests

- [x] 5.1 Unit tests: `CliParser` (verbs, options, fall-through, usage errors), `ApiAddRequest` parsing/validation, response serialization shapes, single-instance `add:{json}` prefix round-trip, config default-on + one-time migration
- [x] 5.2 Headless end-to-end test: start `LocalApiService` against a real `DownloadManager`, hit `/api/add` (start=false) + `/api/list` + a control verb over loopback HTTP, assert item state; verify legacy `/add?url=` + `/ping` responses unchanged
- [x] 5.3 Full suite green: `dotnet test` (build 0 warnings/0 errors)

## 6. End-user documentation & wrap-up

- [x] 6.1 Add a non-technical **"Automation & browser integration"** section to the README (what it is, on-by-default + loopback-only, one CLI example, link to the reference)
- [x] 6.2 Write `docs/local-api.md` reference: every endpoint + params, CLI verbs, exit codes, copy-paste Node.js/Bun `fetch` + `curl` + CLI examples; note the Windows console caveat
- [x] 6.3 Screenshots: capture re-run on macOS produced environment-wide font diffs in ALL views (existing PNGs are Ubuntu-rendered), so they were reverted — re-run `DLDESKTOP_CAPTURE=1` on the Linux box to refresh the settings shots. Code committed to `develop` + pushed; issue #2 comment posted.
