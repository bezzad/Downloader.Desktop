# Tasks

Baseline (develop, 525 tests green): raw **51.5%**, real C# code **61.2%**.
After task 1 (generated code excluded from the denominator): **65.0%** (5849/9000), same 525 tests.
**Final: 883 tests green.** On the full scope, **74.5%** (6715/9018). After the author's scope
decision (views + platform files excluded, 2026-08-27), **78.5%** (6359/8104) — and **82.7% across
the code the suite can guard**. See "Scope decision applied" and "Where the remaining gap is" below.

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

- [ ] 3.1 `FileService` — **not done, deliberately.** Its config path is a private static resolved at
      type-init from the real `%AppData%`/`~/.config`, so exercising the save path would overwrite the
      developer's own `config.json`. Not worth a redirection seam in production code for 31 lines.
- [x] 3.2 `ShutdownService` — schedule/cancel via the `PowerOffOverride` seam. Was reported 0% because
      `ShutdownVerificationTests` is gated behind `DLDESKTOP_VERIFY=1` and silently returns in a normal
      run — it passed without executing anything. The new tests are ungated.
- [x] 3.3 `CliRunner` — the HTTP verbs against a stub bound across the API port range. The `add` verb is
      left alone: with no instance holding the lock it calls `Process.Start(Environment.ProcessPath)`,
      i.e. it would launch a real GUI from the test run.
- [ ] 3.4 `StartupService` — **read path only.** `Apply(true/false)` writes a real autostart entry
      (`~/.config/autostart`, the HKCU Run key, a LaunchAgent); a test must not turn a developer's
      "launch at login" setting on or off behind their back.
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
- [ ] 7.4 `DialogHelper` — **partially.** The modal-tracking seam was already covered; the rest is
      `ShowDialog` against real windows.

## 8. Close out

- [x] 8.1 Full solution rebuild: `0 Warning(s)`.
- [x] 8.2 Full suite green, bounded run.
- [x] 8.3 Browser-extension unit suite green (untouched by this change).
- [x] 8.4 Final coverage measured and reported; SKILL.md updated.

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
