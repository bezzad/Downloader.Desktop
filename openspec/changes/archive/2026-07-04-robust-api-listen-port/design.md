## Context

`LocalApiService` (`Services/LocalApiService.cs`) is a plain `HttpListener` bound once to `http://127.0.0.1:15151/` (`const int Port = 15151`). `Start()` already fails soft (`try/catch`, logs and leaves `_listener=null`) if the bind throws — so a taken port doesn't crash the app today, it just silently disables the whole local-API/extension bridge with no user-visible signal. Three consumers hardcode `15151`:
- `src/browser-extension/manifest.json` / `manifest.firefox.json`: MV3 `host_permissions` list `http://127.0.0.1:15151/*` and `http://localhost:15151/*` (static, install-time declarations — Chrome/Firefox block `fetch` to any origin not declared here, no runtime bypass).
- `src/browser-extension/common.js`: `const APP_PORT = 15151` used to build every `/ping`, `/add`, `/api/add` URL.
- `Services/CliRunner.cs`: builds request URLs against `LocalApiService.Port`.

`SingleInstanceService` separately uses port `15152` as a pure IPC lock — unrelated to this change, not touched.

## Goals / Non-Goals

**Goals:**
- Settings surfaces the effective listen port and a live reachable/not-reachable status.
- If `15151` is already taken by another process at startup, the app falls back to another port automatically, from a small **pre-declared** range, and tells the user once via notification.
- The browser extension and the CLI both keep working after a fallback, without requiring a new extension release for every possible fallback port.

**Non-Goals:**
- Not "bind to any free ephemeral port" — Manifest V3 `host_permissions` must be static, so an arbitrary runtime port is fundamentally unreachable to the extension no matter what the app does. This change intentionally works only within a small fixed range declared in the manifest.
- Not migrating to native messaging (would remove the port problem entirely, but is a much larger lift — new host manifests per browser/OS, install-time registration — out of scope here per the research; noted as a future option, not pursued now).
- No change to what the API/extension endpoints actually do (`/ping`, `/add`, `/api/*` behavior is unchanged) — only how the port is chosen and discovered.

## Decisions

- **Declare a small fixed range `15151`–`15155` in both manifests' `host_permissions`.** This is the only way an extension can ever reach a fallback port under MV3 — the range must be known at install/update time. Five ports is enough headroom for "something else grabbed 15151" without meaningfully widening the permission footprint users see on install.
- **`LocalApiService.Start()` tries the range in order** (`15151` first, then `15152`...`15155`), stopping at the first successful bind. The effective bound port is exposed via a new `LocalApiService.EffectivePort` (renamed from the fixed `Port` constant, which becomes the range's first/preferred value) and persisted into `Config` so a restart prefers the last-known-good port before falling back further (reduces churn across restarts if 15151 is durably taken by something else).
- **Extension discovery**: `common.js` stops assuming `APP_PORT = 15151` fixed. Instead it tries `/ping` against each port in the same declared range in order, starting from whatever port worked last time (cached in extension storage), and updates that cache on success. This mirrors the app's own preference order so in the common case (15151 free) there's no extra latency — the first probe succeeds.
- **CLI (`CliRunner`) resolves the effective port the same way the app does**: since the CLI and the app run as the same install and the app already persists the effective port to `Config`'s JSON file, `CliRunner` reads that file directly rather than re-implementing a port probe — simpler and avoids a second discovery mechanism to keep in sync.
- **One-time user notification on fallback**: reuse the existing `NotificationService` (already used for download complete/fail, update-ready) rather than introducing a new notification channel — fires once per app session when the effective port differs from the preferred `15151`.
- **Settings row is read-only status, not a configurable input** for this change: showing "Local API: 127.0.0.1:15153 (connected)" is the goal; letting the user manually type an arbitrary port would reopen the same MV3 range problem (a manually-chosen port outside the declared range would be invisible to the extension), so manual override is deliberately not offered here.

## Risks / Trade-offs

- [All 5 ports in the range are taken by other processes] → Mitigation: `Start()` still fails soft exactly like today (logs, `_listener=null`, rest of the app unaffected); Settings shows "not running" status so the user isn't left guessing why the extension can't connect.
- [Widening `host_permissions` to 5 ports instead of 1 is a slightly bigger install-time permission surface] → Mitigation: still scoped to loopback-only (`127.0.0.1`/`localhost`), same trust boundary as today, just 5 addresses instead of 1 — not a meaningfully different user-facing prompt.
- [Extension probing multiple ports adds latency to `/ping` when the cached port is stale] → Mitigation: cache and try the last-known-good port first; worst case (cold cache, first probe wrong) is a few sequential failed fetches capped at 5, each with a short timeout.
- [CLI reading `Config`'s JSON directly duplicates knowledge of its file path/shape] → Mitigation: `CliRunner` already runs in-process with access to the same `Config`/`FileService` types the app uses — no new duplication, just reading the already-loaded config instead of a hardcoded constant.
