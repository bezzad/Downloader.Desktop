# Tasks

Baseline (develop, 525 tests green): raw **51.5%**, real C# code **61.2%**.
After task 1 (generated code excluded from the denominator): **65.0%** (5849/9000), same 525 tests.

## 1. Make the measurement honest

- [x] 1.1 Add `src/coverlet.runsettings` excluding generated output (`**/obj/**`, `*.g.cs`,
      `[*]*.RegexGenerator*`) and attribute-excluding `GeneratedCodeAttribute`/`ExcludeFromCodeCoverage`.
- [x] 1.2 Diagnosed: **intermittent coverlet hit-flush loss**, not a test or repo defect — the same
      113-test filter reported the Website plugin at 0% on one run and fully covered on the next.
      Not fixable here; recorded in proposal.md + SKILL.md so a surprising 0% is re-run, not chased.
- [x] 1.3 Point CI's `dotnet test` at the runsettings so codecov and local runs agree.
- [x] 1.4 Re-baseline and record the corrected starting number.

## 2. Dialog/small view models at 0%

- [ ] 2.1 `AboutViewModel` — version text, canonical links, commands.
- [ ] 2.2 `DonateViewModel` — addresses, copy/open commands.
- [ ] 2.3 `ShutdownViewModel` — countdown tick, cancel, shut-down-now.
- [ ] 2.4 `UpdatePromptViewModel` — accept/later result.

## 3. Services at or near 0%

- [ ] 3.1 `FileService` — load/save round-trip, missing file → defaults, corrupt JSON tolerated,
      atomic write.
- [ ] 3.2 `ShutdownService` — schedule/cancel, `PowerOffOverride` seam, notify gating.
- [ ] 3.3 `CliRunner` — port resolution, verb dispatch against a loopback API.
- [ ] 3.4 `StartupService` — the pure/XDG-file path (Linux autostart write/read/remove).
- [ ] 3.5 `ThemeService` — accent application, shade computation, persisted round-trip.

## 4. Update stack

- [ ] 4.1 `UpdateService` — asset-name selection per RID, release-JSON parsing, `IsNewer`/`Normalize`
      edge cases, swap-script generation.
- [ ] 4.2 `UpdateFlow` — the state machine (Idle→Available→Downloading→Ready), cancel, failure
      handling, `IsManagedExternally` under snap.

## 5. Settings + main view models

- [ ] 5.1 `SettingViewModel` — write-through setters that must "bite" (`MaxConcurrentDownloads` →
      default queue + pump, `MaxSpeedKbPerSecond` → `ApplyGlobalSpeedLimit`), reset-to-defaults,
      language/accent selection, tray/startup coupling.
- [ ] 5.2 `MainViewModel` — navigation flags, filter counts/buckets, autosave debounce, capture-URL
      path, all-complete handling.

## 6. Row, details and plugin view models

- [ ] 6.1 `DownloadDetailsViewModel` — part seeding/reconciliation, speed-limit read-back
      (0/`long.MaxValue` = unlimited), mirror editor, refresh-link paths via the internal seams.
- [ ] 6.2 `DownloadItemViewModel` — remaining status/format/tooltip branches.
- [ ] 6.3 `PluginsViewModel` — install feedback, enable/disable, remove, catalog rows.

## 7. Manager, plans and catalog remainder

- [ ] 7.1 `DownloadManager` — remaining transition guards, queue moves/reorder, schedule evaluation.
- [ ] 7.2 `DownloadManager.Plans` — part completion detection, naming normalisation, progress math.
- [ ] 7.3 `PluginCatalogService` — min-app-version gating, catalog parse, install/update flow.
- [ ] 7.4 `DialogHelper` — the pure/testable seams (modal tracking already partly covered).

## 8. Close out

- [ ] 8.1 Full solution build: `0 Warning(s)`.
- [ ] 8.2 Full suite green, bounded run.
- [ ] 8.3 Browser-extension suites still green (`node --test`, Playwright `--workers=1`).
- [ ] 8.4 Final coverage measured and reported; SKILL.md updated with what was learned.
