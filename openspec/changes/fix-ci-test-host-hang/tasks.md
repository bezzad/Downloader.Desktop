# Tasks — fix-ci-test-host-hang

## 1. Make a hang leave evidence (lands first, independent of any root cause)

- [x] 1.1 `.github/workflows/dotnet-desktop.yml`: the `Test` command runs through `scripts/ci-test.sh`,
  which bounds the run itself so a stall fails the STEP instead of letting the job be cancelled; the step
  `timeout-minutes` (20) and the job timeout (30) stay as outer nets. The deadline is a pure-bash
  watchdog, not `timeout` — GNU coreutils `timeout` is not on the macOS runners.
- [x] 1.2 On the timeout path the script collects a managed dump of the live `testhost` into
  `./TestResults` (installs `dotnet-dump` on demand, finds the process by `--name` since pid lookup has
  no portable spelling across the three runners). The existing `if: always()` upload carries it out.
- [ ] 1.3 Prove the harness works: push once with a deliberately over-short deadline
  (`CI_TEST_DEADLINE_SECONDS`), confirm on windows-latest AND macos-latest that the step fails at that
  deadline, the log survives, and the artifact holds the dump. Then restore the real deadline.

## 2. Measure the leaked-scheduler-timer hypothesis (before touching production code)

- [x] 2.1 Instrumented locally (not committed): counted `StartScheduler` calls and `OnSchedulerTick`
  invocations per manager instance.
- [x] 2.2 Ran the full suite with the exact CI command (coverlet runsettings, `--blame-hang`, bounded,
  `taskset -c 0,1`, nothing else live in the tree).
- [x] 2.3 Result, recorded in `design.md`: the hypothesis HOLDS — **405** scheduler timers started per
  run, ticks arriving in bursts from dozens of distinct manager instances after their tests had ended.
  After the fix: **39** (the tests that genuinely configure a schedule).

## 3. Stop leaking the scheduler timer

- [x] 3.1 Failing-first test `UI/SchedulerLifetimeTests` — 5 of its 6 tests fail on the old code
  (verified by temporarily restoring the unconditional `StartScheduler()`), all 6 pass with the fix.
- [x] 3.2 `Services/DownloadManager.cs`: `SyncScheduler()` runs the timer only while `Config.Schedules`
  is non-empty; `StopScheduler()` stops and unhooks it; `Dispose()` releases the scheduler and UI-pump
  timers (`DownloadManager` is now `IDisposable`).
- [x] 3.3 `SchedulerViewModel` calls `SyncScheduler()` after adding/removing a schedule, so a schedule
  added in-session still fires; the existing scheduler tests stay green.
- [x] 3.4 **Deliberately NOT done — the 133 `Initialize` call sites are not touched.** With the lazy
  start, a manager built from a schedule-less `Config.New()` (nearly every test) never starts a timer at
  all, so the leak is gone at its source; rewriting 133 sites to add teardown would be the "remember to
  clean up" shape that caused the bug, and it violates the repo's minimal-change rule. The remaining 39
  come from tests that intentionally configure a schedule.
- [x] 3.5 Full solution `-t:Rebuild` → **0 warnings**; full `dotnet test` → **1728/1728 green**, no
  crash or hang dump written.

## 4. The cause the evidence actually named: a cancelled shutdown countdown powered the machine off

Found on 2026-09-09 because the author noticed the machine shutting down ~1 minute after every "continue"
and asked whether a command contained a shutdown. None did — the suite did it. Full chain in `design.md`.

- [x] 4.1 `ShutdownService.Close()` stops the countdown (`ShutdownViewModel.StopCountdown()`) before
  closing the window. The countdown is a `DispatcherTimer` on the dispatcher, not on the window, so
  closing the dialog left it ticking; under `PerAssembly` it outlived its test, reached zero, and ran the
  real `systemctl poweroff` / `shutdown /s /t 0` / `osascript … shut down` once the arming test's
  `finally` had cleared the stubs. **Also a production bug:** cancelling from the tray shut the user's
  machine down 30 s later anyway.
- [x] 4.2 `UI/ShutdownCancelTests` fails on the old code (`'systemctl' should not have run`) and passes
  with the fix. Two earlier drafts of this test passed against the buggy code — a 3-second pump against a
  30-second countdown, then assertions that removed the very stub that would observe the fire — so the
  countdown length is a seam (`ShutdownService.CountdownSeconds`) and the observation sits at the
  launcher, one layer below the override that masked it.
- [x] 4.3 `ShellLauncher.RealProcessStartBlocked` + `TestSupport/NoRealPowerOff` (`[ModuleInitializer]`,
  same pattern as `NoRealNotifications`): the suite can no longer start a real process, so the next leak
  of this class fails a test instead of taking a machine down. `AllowRealProcessStart()` is the explicit
  opt-out for the three `Unit/RevealInFolderTests` cases that mean to run `/bin/false`, `sleep` and a
  missing command — without it they passed for the wrong reason, which is worse than failing.
- [ ] 4.4 Confirm on CI that this was the hang: the power-off explanation fits the signature the
  scheduler timer never did (only Windows/macOS, which accept a power-off; ubuntu's runner refuses it;
  no `[FAIL]`, no dump, log destroyed). Proof is task 5.1, not an argument.

## 5. Prove it, then write it down

- [ ] 5.1 Five consecutive `.NET Desktop` runs on `develop` with all 6 legs green and **no re-runs**
  (the failure rate was ~1 leg in 6, so one green run proves nothing).
- [ ] 5.2 Replace the SKILL.md section "The hang got WORSE after PerAssembly, and every test leaks a
  scheduler timer (open)" with what was measured and fixed, including the shutdown chain and the
  process-start block.
- [ ] 5.3 `/opsx:sync` the `resource-management` delta, then `/opsx:archive` this change.
