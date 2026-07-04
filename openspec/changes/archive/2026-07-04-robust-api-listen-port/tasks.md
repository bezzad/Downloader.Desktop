## 1. App-side port fallback
- [x] 1.1 `LocalApiService`: replaced the single `Port` constant with `PreferredPort` (15151) + `PortRange` (15151–15155); `Start()` iterates `CandidatePorts()` (persisted last-known-good first if it's inside the range, then the range in order) and binds the first that succeeds; all candidates taken → same soft-fail as before (`_listener=null`, `EffectivePort=0`).
- [x] 1.2 Exposed `LocalApiService.EffectivePort` and persisted the bound port in `DownloadSettings.LocalApiPort` (saved through the app's existing autosave; 0 = not yet determined). Existing tests using the removed `Port` constant were updated to `EffectivePort`.
- [x] 1.3 One-time fallback notification wired in `MainViewModel.SetupAppShell` (runs once per session): when `IsRunning && EffectivePort != PreferredPort`, `NotificationService.Notify` fires with new i18n keys `LocalApi_PortChangedTitle`/`LocalApi_PortChangedMsg`.
- [x] 1.4 `CliRunner.RunHttp` now iterates `ResolveCandidatePorts()` — the persisted `LocalApiPort` read from the same config file (via `FileService`), then the rest of the declared range — stopping at the first port that answers (an HTTP error status from a reached app reports immediately, it doesn't keep probing).

## 2. Settings UI
- [x] 2.1 Read-only "Local API address" row added to `SettingView` (visible only when the integration toggle is on): shows `127.0.0.1:<effective port>` + a green/gray status dot (`LocalApiStatusBrush`, same palette as the download status dots) + connected/not-running text. Backed by `SettingViewModel.LocalApiAddress`/`LocalApiStatusText`/`IsLocalApiRunning`, re-raised when the toggle starts/stops the listener. New i18n keys `Set_LocalApiStatus`/`Set_LocalApiConnected`/`Set_LocalApiOffline` (en; other locales fall back).

## 3. Browser extension
- [x] 3.1 Both manifests widened to all 5 ports × {`127.0.0.1`, `localhost`} in `host_permissions`.
- [x] 3.2 `common.js`: `APP_PORT`/`APP_BASE` replaced by `APP_PORT_RANGE` + `candidatePorts(cached)` (pure) + `discoverAppPort(probe, cachedPort)` (probes `/ping` across the range, last-known-good `appPort` from extension storage first, refreshes the cache on success). `sendToApp` discovers the port then uses `appBase(port)` for both `/api/add` and the legacy `/add`; `pingApp()` = "discovery found any port". `background.js`/`popup.js` needed no changes (they only call `sendToApp`/`pingApp`, whose signatures are unchanged).
- [x] 3.3 `common.test.js`: +6 tests (candidate ordering, out-of-range cache ignored, preferred-responds-first with no extra probes, fallback found after preferred fails, cache-hit single probe, null after full-range miss) — 24/24 green via `node --test`.

## 4. Tests
- [x] 4.1 `Start_falls_back_to_next_port_when_preferred_is_taken` — pre-binds 15151 with a throwaway `HttpListener`, asserts a different in-range port binds and is persisted.
- [x] 4.2 `Start_prefers_the_persisted_effective_port` — a config remembering 15153 binds 15153 first and round-trips unchanged. Full .NET suite 185/185 green.
- [x] 4.3 Extension discovery fallback covered by the mocked-probe tests in 3.3 (Playwright e2e suite runs in the final apply-session sweep).

## 5. Wrap-up
- [x] 5.1 The pre-bind → fallback scenario is exercised end-to-end (real `HttpListener` binds) by test 4.1 rather than a manual GUI run: this box is the author's real machine, and running the real app against a blocked 15151 would persist a fallback port into the author's real `~/.config/Downloader/config.json` (by design the app then stays on it), so the manual variant was deliberately skipped. Settings-row rendering is plain bindings verified by build + headless suite. NOTE for the author: screenshots were NOT regenerated because this session runs on macOS and `docs/screenshots/` must only be regenerated on the Linux box (font rendering differs — see SKILL.md); the Settings page gained a new row below the fold, worth a Linux-side capture refresh later.
- [x] 5.2 Updated `PUBLISHING.md` (permission justification now names the 15151–15155 range and why), `PRIVACY.md` (range + the new `appPort` storage preference), the extension `README.md` (requirements + endpoints sections), and `docs/local-api.md` (fallback behavior + how scripts should discover the port).
