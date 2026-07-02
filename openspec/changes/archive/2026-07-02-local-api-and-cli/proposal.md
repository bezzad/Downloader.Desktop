# Local API and CLI (programmatic task submission)

GitHub issue: https://github.com/bezzad/Downloader.Desktop/issues/2

## Why

Issue #2 asks for a way to submit download tasks programmatically (from Node.js/Bun or any script) instead of clicking through the GUI — e.g. batch-adding hundreds of links read from a database. Today the only external entry points are the browser extension (`GET /add?url=` — opens the Add dialog, requires a user at the machine) and forwarding one bare URL through the single-instance channel. There is no way to set the file name or save folder, no silent add, and no way to observe or control downloads from a script.

## What Changes

- Extend the existing loopback listener (`127.0.0.1:15151`, `BrowserIntegrationService`) into a small **local JSON API** with full control:
  - `POST/GET /api/add` — url (required), filename, path (save folder), queue, `start=false` opt-out; adds **silently and auto-starts** (respecting the queue cap), no dialog.
  - `GET /api/list` — all downloads with id, name, status, progress, size, speed.
  - `POST /api/pause|resume|cancel|retry|remove` — per-item control by id.
  - `GET /ping` and `GET /add?url=` keep their exact current behavior (browser extension unaffected).
- Add a **CLI** on the main binary: `downloader add --url … [--filename …] [--path …] [--queue …] [--no-start]`, plus `list`, `pause|resume|cancel|retry|remove <id>`. `add` forwards to the running instance (or starts the app and adds on startup); control commands talk to the local API and fail with a friendly message when the app isn't running.
- Security model (author decision): **loopback-only, no token**. The integration toggle is now **ON by default** (was opt-in/off) so the API and browser extension work out of the box; existing installs are migrated on once. Settings label updated to reflect that it now also enables the local API.
- **Browser extension** (`src/browser-extension/`) updates so it benefits from the new API:
  - Point captures at the new **silent** `/api/add` endpoint (adds + auto-starts, no dialog) with a popup toggle "Add silently / Open dialog"; the legacy `/add?url=` remains as the dialog fallback.
  - Pass the page-provided suggested filename when available; keep the `/ping` reachability dot.
  - Bump the extension version and refresh its `README.md` / `PRIVACY.md` / `PUBLISHING.md` for the new behavior.
- **End-user documentation** (this is a headline ask): a short, non-technical **"Automation & browser integration"** section in the README explaining what the feature is and how to use it, plus a `docs/local-api.md` reference (endpoints, CLI verbs, exit codes, Node.js/Bun + curl examples) and a one-line in-app Settings description.

## Capabilities

### New Capabilities
- `local-api`: loopback HTTP JSON API — silent add with filename/path/queue, list/status, and per-item control endpoints; enabled by default; unchanged legacy extension endpoints.
- `cli`: command-line subcommands on the main binary that submit/control downloads via the running instance (or launch it for `add`).
- `browser-extension`: the companion extension uses the silent `/api/add` endpoint with a silent-vs-dialog popup toggle, forwards a suggested filename, and keeps the reachability check.

### Modified Capabilities

_None — no existing spec's requirements change (there is no existing spec for the browser extension, the local listener, or the CLI today; all three are introduced here as new capabilities)._

## Impact

- `Services/BrowserIntegrationService` (or a successor `LocalApiService`) — request routing, JSON serialization, UI-thread dispatch into `IDownloadManager`.
- `Services/SingleInstanceService` — forward structured `add` payloads (not just a bare URL).
- `Program.cs` / `MainViewModel` — CLI argument parsing, startup-add handling, Windows console attach for CLI output.
- `Models/DownloadSettings` + `SettingView` — toggle default flips to on, one-time migration for existing configs, label/description; i18n keys (`en.json` + packs).
- `src/browser-extension/` — `common.js` (silent `/api/add`), `popup.*` (silent-vs-dialog toggle), `manifest*.json` version bump, extension docs.
- `README.md` + new `docs/local-api.md` — end-user automation section and reference docs.
- Tests: pure request-parsing/serialization unit tests + headless end-to-end API tests.
- No new dependencies; no changes to the `Downloader` engine or the plugin SDK.
