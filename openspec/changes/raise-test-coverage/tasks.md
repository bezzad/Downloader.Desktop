# Tasks

Baseline (develop, 525 tests green): raw **51.5%**, real C# code **61.2%**.
After task 1 (generated code excluded from the denominator): **65.0%** (5849/9000), same 525 tests.
**Final: 74.0% overall (6693/9041) with 883 tests** — and **82.3% across the code the suite can
meaningfully guard** (see "Where the remaining gap is" below).

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

## Where the remaining gap is

Grouping every uncovered line by why it is uncovered:

| category | covered/total | rate |
|---|---|---|
| **code the suite can guard** | 5962 / 7246 | **82.3%** |
| network / modal-dialog flows | 268 / 659 | 40.7% |
| view code-behind / real windows | 268 / 632 | 42.4% |
| platform OS integration (Windows/macOS notifiers, tray, autostart) | 195 / 450 | 43.3% |
| real user config file | 0 / 31 | 0% |
| app bootstrap (`Program.cs`, `App.axaml.cs`) | 0 / 23 | 0% |
| **total** | 6693 / 9041 | **74.0%** |

The 80% target is not reachable on the current measurement without one of two author decisions:

1. **Narrow what is measured** to the code the suite can guard — excluding `Views/**`, the
   platform-specific notifiers/tray/autostart, and the bootstrap — which reports ~82% today. This is
   common practice, but it is a judgement call about what the number should mean, so it was not done
   unilaterally.
2. **Add production seams for network and OS work** (an injectable `HttpClient`/base URL on
   `UpdateService`/`PluginCatalogService`, a redirectable config path on `FileService`). That buys
   real coverage of the update and catalog flows, but it changes shipping code to suit the tests,
   which the repo's "smallest change, no speculative abstractions" rule argues against.

Windows- and macOS-only paths (`WindowsNotifier`, `StartMenuShortcut`, `MacNotifier`, the update
self-swap) cannot execute on the Linux CI box at all and would need a Windows/macOS CI leg.
