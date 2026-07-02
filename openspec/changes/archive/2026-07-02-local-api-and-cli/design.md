# Design — Local API and CLI

## Context

Issue #2 wants scripts (Node.js/Bun/anything) to submit and manage downloads. The app already has:
- `BrowserIntegrationService`: opt-in `HttpListener` on `127.0.0.1:15151` — `GET /add?url=` (opens the Add dialog) + `GET /ping`. Permissive CORS. Default off (`DownloadSettings` toggle).
- `SingleInstanceService`: loopback lock on port 15152; a second launch forwards its first URL arg (plain text line) to the primary and exits.
- `IDownloadManager`: everything the API needs already exists — `Add(DownloadItem, autoStart)`, `Pause/Resume/Cancel/Retry/Remove(vm)`, `Items` (each `DownloadItem` has a stable `Guid Id`), queue-cap enforcement via `PumpQueue`.

Author decisions (locked via Q&A + follow-up): ship **both** HTTP API and CLI; adds are **silent + auto-start** (opt-out flag); **full control** surface (add/list/pause/resume/cancel/retry/remove); **no token, loopback-only**; the integration toggle is now **ON by default** (was opt-in) so the feature works out of the box; the browser extension is updated to use the new silent endpoint; and end-user docs are a first-class deliverable.

## Goals / Non-Goals

**Goals:**
- A local JSON API on the existing 15151 listener: silent add with url/filename/path/queue/start, list with live status, per-item control by id.
- CLI verbs on the main binary (`add`, `list`, `pause`, `resume`, `cancel`, `retry`, `remove`) usable from scripts on Windows/Linux/macOS.
- Feature works out of the box: integration enabled by default (new installs + one-time migration of existing configs).
- Browser extension uses the silent `/api/add` path (with a dialog fallback the user can pick) and forwards a suggested filename.
- End users can discover and understand the feature: README automation section, `docs/local-api.md` reference, in-app Settings description.
- Zero breakage of the shipped browser extension's existing `/add?url=` + `/ping` behavior (kept as the dialog fallback).
- Testable: pure parsing/serialization helpers + a real end-to-end loopback test.

**Non-Goals:**
- No authentication/token (author decision) and no non-loopback binding.
- No WebSocket/event push — scripts poll `list`.
- No separate CLI executable, no changes to the `Downloader` engine or plugin SDK.
- No API versioning ceremony beyond the `/api/` prefix.

## Decisions

1. **Evolve `BrowserIntegrationService` into `LocalApiService`** (same file renamed, same port 15151, same start/stop wiring and opt-in toggle). One listener serves the legacy extension endpoints unchanged plus the new `/api/*` routes. Alternative — a second listener on a new port — rejected: two sockets, two toggles, more to explain to end users. Keep the persisted setting property name (`DownloadSettings`) for config compatibility; only the Settings label/description text changes ("Browser integration & local API").

2. **Route set** (JSON responses, `Content-Type: application/json`):
   - `POST /api/add` (JSON body) and `GET /api/add` (query params, script convenience): `url` (required, http/https), `filename`, `path` (save folder; default = the configured save path), `queue` (queue name or id; default = default queue), `mirrors` (extra URLs, POST only), `start` (default `true`). Response `201 {"id":"<guid>","name":…,"status":…}`; `400` with `{"error":…}` on bad input.
   - `GET /api/list`: array of `{id, name, url, status, progress, size, downloaded, speed, folder, filePath, queue}`.
   - `POST /api/pause|resume|cancel|retry|remove` with `{"id":"<guid>"}` (or `?id=` on GET for scripts): `200 {"ok":true}`; `404` unknown id. State guards already live in `DownloadManager`, so an inapplicable action is a safe no-op → still `200` (idempotent, script-friendly).
   - Legacy `/add?url=` and `/ping`: untouched code path (dialog + 200).
3. **UI-thread bridging**: HTTP handlers run on the listener thread; every `IDownloadManager` call is wrapped in `await Dispatcher.UIThread.InvokeAsync(...)` and the result (new item id, found/not-found) is awaited so the HTTP response reflects the real outcome. This mirrors the existing `OnUrlCaptured` dispatch pattern but returns values instead of fire-and-forget.

4. **CORS**: legacy endpoints keep `Access-Control-Allow-Origin: *` (the extension depends on today's behavior). The new `/api/*` routes deliberately send **no CORS headers** — with no token, this at least stops random web pages from reading responses; scripts/CLI (non-browser clients) are unaffected. Trade-off accepted by the author (no-token decision).

5. **CLI = the same binary, verb-first args**, parsed in `Program.Main` **before** Avalonia starts. A pure `CliParser.TryParse(args)` (testable) recognizes `add --url … [--filename …] [--path …] [--queue …] [--no-start]`, `list`, and `pause|resume|cancel|retry|remove <id>`; anything else (including today's bare-URL and `--minimized` launches) falls through to the normal GUI path.
   - **`add` always works**: if an instance is running (15152 lock busy), forward a structured payload over the single-instance channel and exit 0. If not running, spawn a detached copy of the app with an internal `--cli-add <json>` argument (GUI starts, performs the silent add at startup) and exit 0 — so a script never blocks on the GUI process.
   - **`list`/control verbs talk HTTP** to `/api/*` on 15151: they need the app running **and** the integration toggle on; otherwise print a friendly one-line error and exit 1. `list` prints the JSON array to stdout (script-first; humans can pipe to `jq`).
   - Exit codes: 0 success, 1 runtime error (app not running / API disabled / unknown id), 2 usage error (prints usage).
6. **Structured single-instance messages**: extend the 15152 line protocol with a prefix — `add:{json}` for a CLI add; a plain `http(s)://…` line keeps meaning "open the Add dialog with this URL" (today's behavior, used by OS re-launch with a URL). `MainViewModel`'s message handler branches on the prefix: structured → silent `DownloadManager.Add`, bare URL → `CaptureUrl` as today.

7. **Windows console output**: the app is a WinExe, so stdout is invisible in a terminal. When CLI verbs are detected on Windows, P/Invoke `AttachConsole(ATTACH_PARENT_PROCESS)` (ignore failure — e.g. double-clicked). Linux/macOS need nothing.

8. **Silent add composition**: build a `DownloadItem { Urls=[url,…mirrors], FileName=filename?, SaveFolder=path??settings default, QueueId=resolved queue??default }` and call `manager.Add(item, autoStart:start)` — identical to what the Add dialog produces, so name auto-resolution, queue cap, persistence and notifications all behave exactly like a UI add.

9. **Enabled by default + migration**: `DownloadSettings` defaults the integration toggle to `true` for new installs. Existing configs persist it as `false`, so a one-time migration is needed to flip them: introduce a small `Config.SchemaVersion` (or reuse an existing versioning hook) and, on load, if the config predates this change, set the toggle to `true` once (respecting a value the user later changes). This avoids the "we can't tell default-false from user-set-false" ambiguity — pre-migration configs are treated as never-having-had-the-setting. Alternative — flip the default without migration — rejected: existing users would keep the listener off and not get the feature. Trade-off: a user who had *deliberately* left it off pre-change gets it on once; acceptable given it's loopback-only.

10. **Browser extension changes** (`src/browser-extension/`): `common.js` gains `sendToAppSilently(url, filename?)` → `GET /api/add?url=…&filename=…` (or POST) which adds without a dialog; `capture()` chooses silent vs the legacy `/add?url=` dialog based on a popup setting stored in `api.storage`. Default = silent (matches the app's silent-add default). The popup (`popup.html`/`popup.js`) gets an "Add silently / Open dialog" toggle and keeps the `/ping` reachability dot. Bump `manifest.json` + `manifest.firefox.json` `version`. The extension is data-only (no build step); it degrades gracefully on an older app (an unknown `/api/add` returns 404 → fall back to `/add?url=`). Store re-submission is the author's (existing accounts) — captured in `PUBLISHING.md`.

11. **End-user documentation**: (a) a README **"Automation & browser integration"** section written for non-developers — what it does ("let scripts or the browser hand links to the app"), the one-liner CLI example, and that it's on by default + loopback-only; (b) `docs/local-api.md` as the reference — every endpoint, params, exit codes, and copy-paste Node.js/Bun `fetch` + `curl` + CLI examples; (c) a short in-app Settings description under the toggle; (d) refreshed extension `README.md`/`PRIVACY.md`.

## Risks / Trade-offs

- [Any local process or webpage can drive downloads — and the toggle is now ON by default] → Author explicitly chose no token and default-on for zero-friction use. Mitigations kept: loopback-only bind, no CORS on `/api/*` (a web page can POST but cannot read responses), path/filename bound by normal file-system permissions, and an easy Settings toggle to turn it off. The window is "add/control downloads," not arbitrary code execution. Documented plainly for users so the trade-off is visible.
- [Default-on migration flips the toggle for someone who deliberately disabled it pre-change] → One-time only; the setting is fully user-controllable afterward and the change is announced in release notes.
- [`path` parameter allows writing anywhere the user can] → Same power the GUI folder picker gives; reject obviously invalid/relative paths with 400 to avoid junk items.
- [Detached self-spawn for `add` when the app isn't running could race (two spawns)] → The single-instance lock resolves it: the loser forwards its payload to the winner; both adds land.
- [HttpListener + JSON body parsing edge cases (encoding, size)] → Cap request bodies (64 KB), UTF-8 only, wrap each request in the existing try/catch-and-keep-listening loop.
- [Windows `AttachConsole` prints after the shell prompt returns] → Cosmetic, standard for WinExe CLIs; document in `docs/local-api.md`.
- [Config compat] → The persisted toggle property name is unchanged; old configs keep working.

## Open Questions

_None — interface shape, add behavior, API scope and security model were all settled with the author before this design._
