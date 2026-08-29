# Tasks

Baseline (develop, 525 tests green): raw **51.5%**, real C# code **61.2%**.
After task 1 (generated code excluded from the denominator): **65.0%** (5849/9000), same 525 tests.
After the author's scope decision (views + platform files excluded, 2026-08-27): **78.5%**.
After the first per-file pass: 951 tests green, 82.7% of the code the suite could guard.
**Final: 1230 tests green, 92.6%** (7313/7901) after round 2 — files under 85% down from 24 to 3.
See "Per-file pass, round 2" for what remains and what blocks each file.

## 1. Make the measurement honest

- [x] 1.1 Add `src/coverlet.runsettings` excluding generated output (`**/obj/**`, `*.g.cs`,
      `[*]*.RegexGenerator*`) and attribute-excluding `GeneratedCodeAttribute`/`ExcludeFromCodeCoverage`.
- [x] 1.2 Diagnosed: **intermittent coverlet hit-flush loss**, not a test or repo defect — the same
      113-test filter reported the Website plugin at 0% on one run and fully covered on the next.
      Not fixable here; recorded in proposal.md + SKILL.md so a surprising 0% is re-run, not chased.
- [x] 1.3 Point CI's `dotnet test` at the runsettings so codecov and local runs agree.
- [x] 1.4 Re-baseline and record the corrected starting number.

## 2. Dialog/small view models at 0%

- [x] 2.1 `AboutViewModel` — version text, canonical links, commands.
- [x] 2.2 `DonateViewModel` — addresses, copy/open commands.
- [x] 2.3 `ShutdownViewModel` — cancel, shut-down-now, localized labels, null-callback design ctor.
- [x] 2.4 `UpdatePromptViewModel` — accept/later result.

## 3. Services at or near 0%

- [x] 3.1 `FileService` — done via `ConfigFileOverride`. (Originally declined: its config path is
      resolved from the real `%AppData%`/`~/.config`, so exercising the save path would overwrite the
      developer's own `config.json`. The redirection seam turned out to pay for itself several times
      over — the CLI's port lookup and the app bootstrap both read that file too.)
- [x] 3.2 `ShutdownService` — schedule/cancel via the `PowerOffOverride` seam. Was reported 0% because
      `ShutdownVerificationTests` is gated behind `DLDESKTOP_VERIFY=1` and silently returns in a normal
      run — it passed without executing anything. The new tests are ungated.
- [x] 3.3 `CliRunner` — the HTTP verbs against a stub bound across the API port range. The `add` verb is
      left alone: with no instance holding the lock it calls `Process.Start(Environment.ProcessPath)`,
      i.e. it would launch a real GUI from the test run.
- [x] 3.4 `StartupService` — read path directly; the write path stays deliberately unexercised
      (`Apply(true/false)` writes a real autostart entry — `~/.config/autostart`, the HKCU Run key, a
      LaunchAgent — and a test must not turn a developer's "launch at login" on or off behind their
      back). `ApplyOverride` lets its CALLER (the app-shell startup path) be tested instead, which is
      where the behaviour that matters lives. The file itself is out of the measurement's scope.
- [x] 3.5 `ThemeService` — accent lookup/fallback, the six Fluent shade overrides, the row-selection tint.

## 4. Update stack

- [x] 4.1 `UpdateService` — version compare, tag normalisation, release-asset selection, and all three
      swap scripts (`BuildUnixScript`/`BuildMacScript` added as internal seams beside the existing
      `BuildWindowsScript`). `CheckAsync` itself is network-bound and stays uncovered.
- [x] 4.2 `UpdateFlow` — the guard clauses and the snap "managed externally" path. The download flow
      itself needs a live asset URL and is not covered.

## 5. Settings + main view models

- [x] 5.1 `SettingViewModel` — the write-through setters that must "bite" (`MaxConcurrentDownloads` →
      default queue + pump, `MaxSpeedKbPerSecond` → global speed limit), language/accent/theme,
      notification and logging toggles, and every pass-through round-tripped. 29% → 52%.
- [x] 5.2 `MainViewModel` — navigation flags, the disjoint filter buckets, bulk actions, status-bar
      aggregates. 33% → 55%. `SetupAppShell`/`InitMainViewModelAsync` need a real window and are not
      covered.

## 6. Row, details and plugin view models

- [x] 6.1 `DownloadDetailsViewModel` — the expired-link refresh through its `ProbeAsync`/`ConfirmAsync`
      seams, mirrors, per-item speed cap, and the per-connection strip driven by a REAL loopback
      download. 49% → 79%.
- [x] 6.2 `DownloadItemViewModel` — the state getters across the full status range, formatting, tooltip.
      72% → 79%; the rest is `RevealInFolder`/`ShellOpen`, which launch a file manager.
- [x] 6.3 `PluginsViewModel` — enable/disable, removal, catalog rows and update badges. 41% → 55%.

## 7. Manager, plans and catalog remainder

- [x] 7.1 `DownloadManager` — queue start/pause/stop, moves, reorder, schedule evaluation (incl. the
      persisted once-per-day latch), and the "already downloaded" completion path driven end to end.
      86% → 93%.
- [x] 7.2 `DownloadManager.Plans` — already had 16 tests; left as is.
- [x] 7.3 `PluginCatalogService` — `ParseCatalog` strictness and the version/min-app-version rules.
- [x] 7.4 `DialogHelper` — done in round 2. `ShowDialog` against real windows became reachable once
      `DesktopLifetimeScope` gave the headless app a classic desktop lifetime. 28% → 88%.

## 8. Close out

- [x] 8.1 Full solution rebuild: `0 Warning(s)`.
- [x] 8.2 Full suite green, bounded run.
- [x] 8.3 Browser-extension unit suite green (untouched by this change).
- [x] 8.4 Final coverage measured and reported; SKILL.md updated.

## Per-file pass, round 2 (author: "every file above 85%", 2026-08-29)

**1230 tests green. 92.6% overall; files under 85% went 24 -> 3.** Measured with
`--settings src/coverlet.runsettings` (see that file for what is in scope and why).

Where it moved, and what was actually wrong:

- **The dialogs had never opened.** Every `DialogHelper` entry point starts with "if there is no main
  window, do nothing", and the headless runtime has no desktop lifetime — so the whole file took its
  early return and read as covered while none of it ran. `TestSupport/DesktopLifetimeScope` installs a
  classic desktop lifetime with a real window for the duration of a test. 28% -> 88%.
- **The CLI tests were passing without executing a line of `CliRunner`.** Their stub bound all five API
  ports to ONE `HttpListener`, which fails to start wholesale if any single port is taken; `BoundCount`
  then stayed 0 and the "nothing to assert against" guard returned early from every test. One listener
  per port, plus steering the CLI's persisted-port lookup at a port the stub owns. 15% -> 54%.
- **The app shell's startup wiring** (tray, close-to-tray, run-at-startup, local API, single-instance
  hand-off, update checks) had never run: applying run-at-startup writes the developer's own autostart
  entry, so no test could be allowed near it. `StartupService.ApplyOverride` removes that.
  `MainViewModel` 57% -> 88%, `App.axaml.cs` 32% -> 49%.
- **The update check was written off as "network"** when only the GitHub lookup is; everything after it
  decides what the user sees. `UpdateFlow.CheckOverride` puts the lookup behind a hook. 70% -> 92%.
- Plugin catalog fetch + install (`PluginCatalogService.ReleasesUrlOverride`, a loopback release serving
  a real plugin zip into a temp plugins root), the plan runner's row-facing half driven end to end,
  ffmpeg provisioning against a stub archive, the Ollama registry's unhappy answers and the local model
  store's refusals, and the offline-copy transfer.

### New seams added (all `internal`, never set by the app)

| seam | unlocks |
|---|---|
| `StartupService.ApplyOverride` | the whole app-shell startup path, without writing the developer's autostart entry |
| `UpdateFlow.CheckOverride` | every branch of the update check after the GitHub lookup |
| `PluginCatalogService.ReleasesUrlOverride` | the catalog fetch, against a loopback release |
| `DialogHelper.{OpenFilePicker,SaveFilePicker,OpenFolderPicker}Override` | what a caller does with a CHOSEN path (install this plugin, export the log here) |
| `SingleInstanceService.Dispatch` (private -> internal) | delivering a forwarded link without a second process |
| `TestSupport/DesktopLifetimeScope` | the dialogs, by giving the headless app a real main window |
| `TestSupport/DeferringScheduler` | the shell's init running AFTER the window is assigned, as it does in the app |

### The 3 files still under 85%, and why each is out of reach

| file | cov | blocker (verified, not assumed) |
|---|---|---|
| `App.axaml.cs` | 49% | The remaining 30 lines are the shutdown hook. It ends in `desktop.Shutdown()`; driving it would shut the test host down. The bootstrap half (services resolve, window built, view model attached) IS now covered. |
| `CliRunner` | 54% | The `add` verb. With no instance holding the lock it calls `Process.Start(Environment.ProcessPath)` — it would launch a real GUI; and forwarding instead would post a download into the developer's *running* app. Every other verb is covered. |
| `NotificationService` | 72% | 9 lines are the macOS and Windows notifier branches. They cannot execute on the Linux runner; the same two notifier FILES are already excluded from the metric for that reason. |

`Program.cs` is now excluded from the measurement outright, on the author's instruction — every line of
it is `Environment.Exit`, claiming the single-instance lock, or handing control to Avalonia's main loop.

### Two things worth not re-deriving

- **A headless `[AvaloniaFact]` runs on the UI thread, so `RxApp.MainThreadScheduler` executes inline.**
  `MainViewModel` schedules its init from its constructor and the app assigns `View` afterwards — so
  under the default scheduler the init runs BEFORE the window is assigned and `SetupAppShell` silently
  skips everything. Install `DeferringScheduler` to get the app's real ordering.
- **A test can pass while testing nothing.** Two separate cases here: an env-gated suite that returns
  immediately (`ShutdownVerificationTests`, found last round) and a fixture guard that swallows its own
  setup failure (`CliRunnerTests`' port stub, found this round). Before writing new tests for a file,
  check whether an existing suite only *looks* like it covers it — the per-file uncovered count is the
  tell.

## Per-file pass (author: "each code must above 80")

951 tests. Overall **82.7%**; files under 80% went **34 -> 20**. What moved:

- Six near-identical `Process.Start(UseShellExecute)` helpers were consolidated into
  `Services/ShellLauncher` with an override seam. Covering those call sites for real would open
  browser tabs and file-manager windows on whoever runs the suite, which is exactly why they had
  never been covered. `NotificationService` and `ShutdownService`'s private copies of the same
  helper fold in too, which made the notification icons and the per-platform power-off command
  assertable for the first time.
- Plugin entry points, the file logger (incl. the engine's ILoggerFactory bridge), URL/name
  resolution, the notch overlay, SDK interface defaults, the countdown timer actually running down,
  Settings' service-backed toggles and reset-to-defaults, the per-connection segment
  (freeze/thaw/sync) and the copy buttons.

### The 20 files still under 80%, by what actually blocks them

**Needs a production seam** (each would install/write to the developer's real machine, or hit the
network, with no way to redirect it):

| file | cov | blocker |
|---|---|---|
| `PluginsViewModel` | 58% | Add/Update/Install write to the real `PluginsRoot`; Install also opens a file picker |
| `PluginCatalogService` | 69% | `FetchAsync`/`DownloadAssetAsync` hit GitHub; `InstallOrUpdateAsync` installs into the real `PluginsRoot` |
| `UpdateFlow` / `UpdateService` | 31% / 55% | GitHub release check, asset download, and the self-swap that replaces the running install |
| `DialogHelper` | 28% | `ShowDialog` against real modal windows |
| `CliRunner` | 15% | the `add` verb calls `Process.Start(Environment.ProcessPath)` — it would launch a real GUI |
| `FileService` | 0% | its config path is a private static resolved from the real `%AppData%`/`~/.config` |
| `BinaryFile` / `FfmpegBinary` | 40% / 75% | download real tool binaries (~80 MB ffmpeg) |
| `OllamaRegistry` / `OllamaInstaller` | 69% / 71% | talk to a real registry / local Ollama daemon |

The unlock is small and specific: a redirectable plugins root and config path, and an injectable
`HttpClient`/base URL on the two services that fetch. That is ~350 of the 513 remaining lines.

**App bootstrap** — only exists once the app really starts: `Program.cs` (0%), `App.axaml.cs` (32%),
and `MainViewModel` (57%, of which 70 lines are `SetupAppShell`: tray + single-instance IPC + local
API + update check, all of which need a live window and OS session).

**Platform branch limited** — only one of several OS branches can execute on the Linux runner:
`NotificationService` (59%), `ShutdownService` (76%), `ShellLauncher` (78%), `NotchService` (77%).

**Just needs more tests**: `DownloadManager.Plans` (72%) and one default-interface line in
`IDownloaderPlugin`.

## Scope decision applied (author's call, 2026-08-27)

`Views/**` and the platform-integration files (Windows/macOS notifiers, the Windows shortcut COM
path, run-at-startup, tray, taskbar progress) are now excluded from the measurement — they need a
specific OS or a live desktop session, and run-at-startup would mutate the developer's real
"launch at login" setting just by running the suite. The views are still *tested*
(`UI/ViewLoadTests`); only the metric's scope changed.

Deliberately kept in scope so this stays a scope decision and not a way to flatter the number:
`SingleInstanceService` (loopback IPC, genuinely tested — 81%), `Program.cs`/`App.axaml.cs`, and
every network-bound service.

**Result: 78.5%** (6359/8104), up from 74.5% on the full scope.

**Correction to an earlier estimate:** this was previously predicted to land at "~82%". That was
wrong — the 82.7% figure was the *"code the suite can guard"* bucket, which also excluded the
network/modal-dialog service files. Views + platform alone gives ~78.5%.

### Measurement variance (important for reading codecov)

Single runs of the same commit report **76–78%**, and one reported 59.8% because two whole plugin
assemblies came back at 0%. Cause, now pinned: the plugin tests load the *same* plugin DLLs into
collectible `AssemblyLoadContext`s that then unload, and coverlet's per-module hit flushing loses
data when that happens — only plugin assemblies are ever affected. Excluding just
`PluginLoadTests`+`PluginReloadTests` from a coverage run gives a stable 77.6–77.9%.

Coverlet only ever *loses* hits, never invents them, so the maximum across runs is the closest
estimate to truth — that is where 78.5% comes from (three runs, max-merged). CI still runs the whole
suite in one invocation (correctness beats a tidier number), so expect its reported figure to wobble
a point or two.

## Where the remaining gap is

With views and platform files excluded, the measured 8104 lines break down as:

| category | covered/total | rate |
|---|---|---|
| **code the suite can guard** | 5995 / 7246 | **82.7%** |
| network / modal-dialog flows (`UpdateFlow`, `UpdateService`, `PluginCatalogService`, `DialogHelper`, `CliRunner`) | 257 / 636 | 40.4% |
| `SingleInstanceService` (kept in scope) | 88 / 109 | 80.7% |
| app bootstrap (`Program.cs`, `App.axaml.cs`) | 19 / 82 | 23.2% |
| real user config file (`FileService`) | 0 / 31 | 0% |
| **total** | 6359 / 8104 | **78.5%** |

Two levers remain for the last ~1.5 points, both the author's call:

1. **Also exclude the network-bound services** (the 636-line row above) — would report ~82%. Weaker
   justification than views/platform: those are ordinary app services that simply lack seams to
   reach, not code the suite is barred from running.
2. **Add seams and test them for real** — an injectable `HttpClient`/base URL on `UpdateService` and
   `PluginCatalogService`, a redirectable config path on `FileService`. This buys genuine coverage of
   the update and catalog flows rather than hiding them, at the cost of changing shipping code to
   suit tests.

Windows- and macOS-only paths are now out of scope entirely; testing them would need a Windows or
macOS CI leg.
