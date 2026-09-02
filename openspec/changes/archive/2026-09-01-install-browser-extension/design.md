## Context

Facts established before this change was written. **Do not re-derive them.**

**The browser side (verified against current browser behaviour):**

| Browser family | Can a desktop app install an extension? |
|---|---|
| Chrome / Edge / Brave / Vivaldi / Opera / Chromium | No. The Windows external-install registry key (`HKLM\…\<Browser>\Extensions\<id>`) and the Linux/macOS `External Extensions` JSON both accept only an `update_url` pointing at the Web Store. Local `.crx` sideloading was removed. |
| Firefox / LibreWolf | No. An `.xpi` must be Mozilla-signed; an unsigned one placed in `distribution/extensions` is silently ignored on release builds. |
| Any | The only force-install path left is **enterprise policy** (`ExtensionInstallForcelist` / `policies.json`), and for Chrome that still requires a store-hosted extension. |

So elevation buys exactly one capability — a write to a browser policy key — which is the browser-hijacker
signature and is scored *higher* when elevated. Issue #4 already cost this project a Bitdefender ATD
quarantine for a far weaker shape (an unsigned exe spawning `powershell.exe`). Not doing it.

**What manual loading actually costs the user**, which is why the dialog must be honest rather than cheerful:

- **Chrome/Edge**: `chrome://extensions` → Developer mode → *Load unpacked*. Works, but Chrome shows a
  *"Disable developer mode extensions"* prompt on **every launch**, there is **no auto-update**, and the
  extension's ID is **derived from the absolute folder path** — moving or deleting the folder breaks the
  extension and loses its stored settings. Hence the permanent path requirement below.
- **Firefox**: `about:debugging` → *Load Temporary Add-on* — **removed on every browser restart**. There is
  no permanent unsigned install on a release build. The dialog must say so rather than imply otherwise;
  Firefox's real answer is the AMO listing, which is already automated (`extension-distribution` spec).
- A managed/enterprise Chrome may have Developer mode disabled entirely. Handle as "we told you the steps
  and they may not apply", not as an error state.

**Repo state that this builds on:**

- `scripts/build-extension.sh` already produces `dist/downloader-extension-chrome.zip` and
  `dist/downloader-extension-firefox.zip`, and `release.yml`'s `extension` job already attaches both to
  every `v*` release. There is **no sha256 manifest** for them — that is the gap this change fills.
- `Services/PluginCatalogService` is the working template: fetch the latest release, read a catalog JSON
  asset, resolve each entry's download URL from that same release's asset list, fail soft (empty list) on
  every error. `PluginManager.InstallFromZipAsync` is the template for download → **verify sha256 before
  extract** → install, and `PluginCatalogService.MeetsMinAppVersion` for the compatibility gate.
- `Services/LocalApiService` serves `/ping`, `/add`, `/api/add`, `/api/can-handle`, `/api/settings` on a
  port in 15151–15155. `/api/settings` already returns `{ defaultSavePath, version }` and is deliberately
  limited to those two fields (the same API *accepts* cookies, so an echo is how a secret would escape) —
  **keep that discipline**: the new handshake adds an inbound field, not an outbound one.
- The extension never sends its own version today (`getManifest()` appears nowhere in `common.js`,
  `background.js` or `popup.js`).
- **The two manifests currently agree (both 1.7.0), but nothing enforces it.** `PUBLISHING.md` says they
  must be bumped together and the AMO workflow's bump guard only watches the Firefox manifest, so a
  one-sided bump would ship a mislabelled Chrome/Edge zip — the code is shared (`build-extension.sh` packs
  the same `common.js`/`popup.js` into both), so only the declared version would be wrong. Task 2.3 makes
  the catalog read each target's version from its manifest, which turns that convention into something a
  test can hold.
- `Unit/NoShellSpawnTests` text-scans app + plugin source and fails the build on shell spawns. Everything
  here must be in-process: `System.IO.Compression.ZipFile`, `Microsoft.Win32.Registry`,
  `File.SetUnixFileMode`. Not `tar.exe`, not `reg.exe`.

## Goals / Non-Goals

**Goals**
- A non-technical user can get the extension into their browser from inside the app, and can *see* that it
  worked.
- The extension can be updated without shipping an app release.
- Zero antivirus surface: no elevation, no shell, no writes outside the app's data directory, no reads of
  any browser profile.

**Non-Goals**
- Automatic install by any mechanism (see Context).
- Firefox self-hosted auto-update via `gecko.update_url` (real, but needs AMO self-distribution signing —
  a separate pipeline, Firefox-only; revisit after this ships).
- Publishing store listings (author-gated).

## Decisions

### D1 — Fetch from the GitHub release; do not bundle the extension in the app

**Decision.** The app downloads `downloader-extension-{chrome,firefox}.zip` from the latest GitHub release,
verifies sha256 against `extension-catalog.json` from the same release, then extracts.

**Why this does not violate the download-then-execute rule.** The rule (CLAUDE.md, issue #4) is about
*third-party binaries this process runs*. These files are inert data: the app never executes them, never
marks anything executable, and never writes into a browser directory. A *different* program reads them
later, under a manual action the user performs in that program's own UI. That is a strictly weaker
signal than the plugin catalog, which downloads a zip and then **loads a managed assembly from it** — and
that already ships.

**Why not bundle.** The author's size argument is weak on its own (~50 KB zipped). The decisive reason is
**update decoupling**: a store-policy fix or a broken-site fix reaches users without an app release. Also,
one source of truth — the release asset — beats "the copy inside the app" plus "the copy on the release",
which would drift exactly the way the two manifests just did.

**Cost, and its mitigation.** Offline users cannot install (clear error + a link, no silent failure), and
the extension can outrun the app's local API. The latter is handled by `minAppVersion` per catalog entry,
enforced before an entry is offered — the same field and the same check the plugin catalog uses.

**Guard rails, all enforced by tests:**
- sha256 verified **before** extraction; a mismatch touches nothing and reports a friendly error.
- Extraction via `System.IO.Compression.ZipFile` only. Entry paths validated against traversal (`..`,
  absolute, drive-qualified) — an entry escaping the destination aborts the whole install.
- No executable bit is ever set on an extracted file.
- The destination is always under the app's data directory. **A browser profile or extension directory is
  never a write target**, and there is a test that says so.

### D2 — Unpack to a permanent, per-target path

`<AppData>/Downloader/extension/<target>/` (`target` ∈ `chrome`, `firefox`), i.e.
`~/.config/Downloader/extension/chrome` on Linux, alongside `plugins/`.

**Why permanent and not temp:** an unpacked Chrome extension's ID is derived from its absolute directory
path. A temp folder means a new ID (and a fresh, empty settings store) on every install, and a broken
extension as soon as the OS cleans temp. This is the single most important detail in the whole change.

Install is **staged**: extract to `<target>.new`, delete `<target>`, move `.new` into place. A crash mid-way
leaves the previous working copy or a clearly-named leftover, never a half-extracted extension the browser
would refuse to load. `installed.json` (`{ target, version, installedAt }`) sits beside it so the app knows
what is on disk without re-reading the manifest.

### D3 — Detection is read-only and hard-coded to a curated list

A `DetectedBrowser { Id, Name, Family, ExecutablePath }` where `Family` ∈ `Chromium | Gecko`.

- **Windows**: `Microsoft.Win32.Registry` — `HKLM`/`HKCU` `SOFTWARE\Clients\StartMenuInternet\*` →
  `shell\open\command`, plus `SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\<exe>`. Not `reg.exe`.
- **Linux**: probe a fixed list of executable names on `PATH` plus known absolute locations
  (`/usr/bin`, `/snap/bin`, `/var/lib/flatpak/exports/bin`, `~/.local/share/flatpak/exports/bin`).
- **macOS**: check for known `.app` bundles in `/Applications` and `~/Applications`.

Curated list: Chrome, Chromium, Edge, Brave, Vivaldi, Opera (Chromium family); Firefox, LibreWolf (Gecko).
A curated list is deliberate — enumerating "anything that looks like a browser" invites false positives and
edges toward profiling the machine.

**Detection reads existence and executable path only.** No profile directory is opened, no cookie store, no
preferences file. This is the same boundary that made the HLS plugin drop `--cookies-from-browser`, and it
is what keeps this feature clear of the infostealer shape.

### D4 — Store link when there is one, unpack when there is not

Per family the dialog resolves a **store URL** from the catalog entry (`storeUrl`, optional). When present,
the primary action launches **that browser's executable at that URL** (absolute path, no shell) and the
manual steps collapse to a hint. When absent — today's state for Chrome/Edge — the primary action is the
unpack flow. So publishing a listing later is a catalog field, not a code change.

Firefox is the asymmetric case: AMO is already automated, and its manual path is *temporary-only*. When the
AMO URL is present Firefox shows only the store action; the manual path stays available behind a "load it
temporarily instead" affordance carrying the honest restart warning.

### D5 — The extension identifies itself on requests it already makes

`common.js` adds two fields to the calls it already sends: `extVersion` (from
`api.runtime.getManifest().version`) and `browser` (a coarse label: `chrome` / `firefox` / `edge`). Sent as
query parameters on the GET forms and JSON fields on the POST form, plus an `X-Downloader-Extension` header
so `/ping` can carry it too. No new permission; no extra request.

`LocalApiService` records the last seen `{ version, browser, at }` in memory (never persisted, never logged
— consistent with the existing "never log the request URL or query string" rule). Settings reads it for the
Connected marker and the version comparison.

**Rejected alternative:** a dedicated `/api/extension-hello` poll. It adds a request, adds a route, and
tells us nothing the existing requests cannot carry.

### D6 — Show, don't claim

The Connected ✓ per browser comes from a real request having arrived from that browser label, not from
"we extracted the files". This is the whole difference between a dialog that helps and a dialog that lies:
the manual steps can fail in ways the app cannot see (Developer mode disabled by policy, user closed the
tab), and a green tick that only means "we unzipped something" would be worse than no tick.

## Risks / Trade-offs

- **The manual path is genuinely poor UX on Chrome** (a startup nag, no auto-update, path-bound ID). We are
  not fixing that — it is a browser policy. Mitigation is honesty in the dialog plus making the store path
  a one-field switch, so this degrades to a footnote the day the listings go live.
- **Firefox manual loading is temporary.** Saying so plainly costs a little polish and saves a bug report.
- **The catalog is one more release artifact that can go stale.** Generated by the same script that builds
  the zips, from the zips, so it cannot disagree with them.
- **Antivirus is a behavioural judgement, not a rulebook.** Everything above lowers the score; nothing
  guarantees it. The Windows binaries remain unsigned, which stays the real root fix (issue #4).

## Migration

None. New feature, opt-in, additive. `extension-catalog.json` is a new release asset; an app running
against an older release that lacks it shows "no build available" and the store/manual instructions,
never an error.

## Open Questions

None blocking. One deferred: Firefox self-hosted auto-update (D1 non-goal) — worth revisiting once AMO
self-distribution signing is set up, since it would make Firefox the one browser that updates itself
from GitHub.
