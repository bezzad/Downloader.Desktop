## 1. App-side port fallback
- [ ] 1.1 `LocalApiService`: replace the single `Port` constant with a small declared range (`15151`–`15155`); `Start()` tries each in order (preferring the last-persisted effective port first, if any), binding to the first that succeeds.
- [ ] 1.2 Expose the effective bound port (e.g. `LocalApiService.EffectivePort`) and persist it in `Config`/`DownloadSettings` so a restart prefers the last-known-good port.
- [ ] 1.3 When the effective port differs from the preferred `15151`, fire a one-time notification via `NotificationService` stating the new port.
- [ ] 1.4 `CliRunner`: read the effective port from the same persisted `Config` instead of the old fixed `Port` constant.

## 2. Settings UI
- [ ] 2.1 Add a read-only "Local API" row to `SettingView`/`SettingViewModel` showing the effective address (`127.0.0.1:<port>`) and a live reachable/not-reachable indicator (reuse the existing `/ping`-style check pattern).

## 3. Browser extension
- [ ] 3.1 `manifest.json` and `manifest.firefox.json`: widen `host_permissions` from the single `15151` origin to the full declared range (`15151`–`15155`, both `127.0.0.1` and `localhost`).
- [ ] 3.2 `common.js`: replace the fixed `APP_PORT`/`APP_BASE` with a small discovery routine that probes `/ping` across the declared range, starting from the last-known-good port cached in extension storage, and updates that cache on success; use the discovered base for `/add`, `/api/add`, and subsequent `/ping` calls.
- [ ] 3.3 Update `common.test.js` for the new discovery logic (pure/mockable parts) and add coverage for "preferred port responds first" and "fallback port is found after preferred fails."

## 4. Tests
- [ ] 4.1 `Downloader.Desktop.Tests`: unit test that `LocalApiService.Start()` falls back to the next port when the preferred one is pre-bound by a throwaway `HttpListener` in the test.
- [ ] 4.2 `Downloader.Desktop.Tests`: unit test that the effective port round-trips through `Config` persistence and is preferred on the next `Start()`.
- [ ] 4.3 `common.test.js`/e2e: extension discovery finds a fallback port when the preferred one is unreachable (mocked `fetch`).

## 5. Wrap-up
- [ ] 5.1 Manually verify: pre-bind `15151` with another process, start the app, confirm it falls back, Settings shows the new port, and a loaded unpacked extension still connects.
- [ ] 5.2 Update `PUBLISHING.md`/store listing copy if the widened `host_permissions` changes what's shown to users on install (Chrome/Firefox review may show the permission list).
