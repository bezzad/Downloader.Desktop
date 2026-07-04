## Why

`LocalApiService` binds a plain `HttpListener` to a hardcoded port `15151`, and both the browser extension (`manifest.json`/`manifest.firefox.json` `host_permissions`, `common.js` `APP_PORT`) and the CLI (`CliRunner`) assume that exact port. Settings gives the user no visibility into this at all — there's no indication which port the API/extension bridge listens on, and if another app on the machine has already reserved 15151, the app's `HttpListener.Start()` throws and the whole local-API/extension bridge silently stops working, with nothing in Settings to explain why or let the user recover.

## What Changes

- Settings gains a small "Local API" info row showing the listen address/port (`127.0.0.1:15151`) and a live reachable/not-reachable status, so users (and their extension's "connected" indicator) have one place to see what's going on.
- At startup, if `15151` is already bound by another process, `LocalApiService` automatically falls back to the next free port from a small **pre-declared range** (`15151`–`15155`) — matching the range already baked into the browser extension's manifest `host_permissions` today's fixed `15151` will be extended to (see Impact) — persists the effective port in `Config`, and shows a one-time notification telling the user which port is now in use.
- Browser-extension side: `manifest.json`/`manifest.firefox.json` `host_permissions` are widened from the single `15151` origin to the full pre-declared range (`15151`–`15155`), and `common.js` probes `/ping` across that range in order (starting from the last-known-good port) to (re)discover the app instead of assuming `15151`.
- The CLI (`CliRunner`) reads the effective port the same way the extension does (probe the small range, or read it from the same `Config` file the desktop app already writes) instead of hardcoding `LocalApiService.Port`.
- This is deliberately scoped to a **small fixed range**, not "any free port," because Chrome/Firefox extension manifests must statically pre-declare which localhost origins they're allowed to reach (Manifest V3 `host_permissions`) — an arbitrary fallback port picked at runtime would be invisible/unreachable to the extension no matter what the app does.
- No change to what runs on the port (existing `/ping`, `/add`, `/api/*` endpoints unchanged) — only how the port is chosen, surfaced, and discovered.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `local-api`: listen port is no longer a hardcoded single value — it falls back within a small declared range when the default is taken, and the effective port is persisted and surfaced.
- `browser-extension`: the extension's reachability check (`/ping`) probes the same declared port range instead of assuming a single fixed port, so it keeps working after a fallback.

## Impact

- `src/Downloader.Desktop/Services/LocalApiService.cs` — bind-with-fallback loop over the declared port range instead of a single `HttpListener.Start()` call; expose the effective bound port.
- `src/Downloader.Desktop/Models/Config.cs` / `DownloadSettings.cs` — persist the effective/last-used port.
- `src/Downloader.Desktop/Views/SettingView.axaml` (+ ViewModel) — new read-only "Local API" status row.
- `src/Downloader.Desktop/Services/CliRunner.cs` — resolve the effective port instead of the `Port` constant.
- `src/browser-extension/manifest.json`, `manifest.firefox.json`, `common.js` — widen `host_permissions` to the declared range and probe it in `/ping` discovery.
- `Downloader.Desktop.Tests` — new tests for the bind-fallback loop (port-in-use simulated by pre-binding 15151 in the test) and for the persisted-port round-trip.
