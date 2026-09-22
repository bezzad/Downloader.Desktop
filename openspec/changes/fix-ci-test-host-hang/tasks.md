# Tasks — fix-ci-test-host-hang

## 1. Make a hang leave evidence (lands first, independent of any root cause)

- [x] 1.1 `.github/workflows/dotnet-desktop.yml`: the `Test` command runs through `scripts/ci-test.sh`,
  which bounds the run itself so a stall fails the STEP instead of letting the job be cancelled; the step
  `timeout-minutes` (20) and the job timeout (30) stay as outer nets. The deadline is a pure-bash
  watchdog, not `timeout` — GNU coreutils `timeout` is not on the macOS runners.
- [x] 1.2 On the timeout path the script collects a managed dump of the live `testhost` into
  `./TestResults` (installs `dotnet-dump` on demand, finds the process by `--name` since pid lookup has
  no portable spelling across the three runners). The existing `if: always()` upload carries it out.
- [x] 1.3 Proven by run **35619203001** (2026-09-21). `CI_TEST_DEADLINE_SECONDS`/`CI_TEST_DUMP_LEAD_SECONDS`
  are now `workflow_dispatch` inputs (`0c19598`) rather than a deliberately-red push to be reverted: empty
  on every push and PR, so the real defaults apply and there is no "restore it" commit to forget. Run with
  150s/60s: windows-latest (Debug+Release) and macos-latest/Debug were killed at the deadline, the step
  FAILED with the log intact, and the Windows artifact holds a real 235 MB `hang-testhost.dmp` beside
  `hang-report.txt`. It also confirmed the `KILLED` sentinel live — a SIGTERM'd `dotnet test` reported
  wait status 0 on ubuntu/macOS and 143 on Windows.
  **The proof also found two defects in the harness, both fixed in `ac110f7`** (see `design.md`): the
  deadline drifted by however long the dump took (macOS/Release outlived its own 150s deadline and
  PASSED), and macOS still fell through to the 6 GB core the script says it avoids (6,182,015,304 bytes,
  1.84 GB per artifact, on both macOS legs).

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
## 6. The other cause: the dispatcher binding race (2026-09-21 — measured, reproduced, fixed)

- [x] 6.1 Measure the hypothesis before touching anything. Two arms, one variable, each in its own
  process (the binding is a process-global one-shot): **A** touched `Dispatcher.UIThread` on the xunit
  thread and then started the session — a single trivial test **hung until killed at 240 s**; **B**, the
  identical control without that touch, **passed in 173 ms**. That is the deterministic local repro this
  hunt never had.
- [x] 6.2 Fix: `TestSupport/HeadlessSessionFirst.cs` starts the session and **dispatches once** before
  any test runs, so `Dispatcher.UIThread` binds to the session's own thread and the order tests happen to
  run in stops mattering. Two earlier attempts are recorded in the file because both look right and are
  not: a `[ModuleInitializer]` also runs in the DISCOVERY process, where building the app blocks xunit
  v3's handshake ("Test process did not respond within 60 seconds"); and starting the session *without*
  dispatching changes nothing, because `EnsureSharedApplication()` is called lazily from `DispatchCore`.
- [x] 6.3 Regression test `Unit/HeadlessSessionBindingTests` — a plain `[Fact]`, deliberately standing
  where the damage was done. Verified in BOTH directions, as the repo's rule requires: with the fix
  disabled it **fails in 15 s** with a readable reason; with it enabled it **passes in 21 ms**. The
  bounded wait is the point — a regression must be a red test, not another silent stall.

- [!] 4.4 **Answered, but not the way it was framed — the power-off was A cause, not THE cause.** It
  explains the runs that died with the log destroyed (only Windows/macOS, which accept a power-off;
  ubuntu's runner refuses it) and it is fixed. But on 2026-09-21 `ThreadAffinityWatch` finally caught the
  *other* fault with a stack (run **35619162399**, ubuntu/Release): `Dispatcher.VerifyAccess()` throwing
  inside `HeadlessUnitTestSession.EnsureSharedApplication()` → the shared session's dispatch loop faults →
  every later test waits on a completion source nothing will set → `Passed: 4`, three minutes of nothing,
  `Test Run Aborted`. That is hang-shaped and it is STILL LIVE.
  Note what it means for `TestAppBuilder`'s reasoning: PerAssembly moved the failing call off the per-test
  path, cutting ~1700 attempts per run to one — it did not remove the failure mode. Leading hypothesis and
  the cheap way to measure it before touching anything are in `design.md`; this change's own history is
  the argument for not fixing it blind.
  **Since measured, reproduced and fixed — see group 6.** The hypothesis held exactly: touching
  `Dispatcher.UIThread` before the session starts hangs the run (240 s kill) where the control passes in
  173 ms. So the honest answer to 4.4 is that there were TWO causes with one shared signature, and both
  are now fixed. Re-verifying on CI is 5.1's job, not an argument's.

## 5. Prove it, then write it down

- [ ] 5.1 Five consecutive `.NET Desktop` runs on `develop` with all 6 legs green and **no re-runs**
  (the failure rate was ~1 leg in 6, so one green run proves nothing).
  **Not yet met — the counter has never got past one, and the clock restarts from the group 6 fix.**
  Three distinct faults were seen on `develop` on 2026-09-21 alone:
  | fault | seen | status |
  |---|---|---|
  | thread-affinity violation faulting the shared headless session (4.4) | 35619162399 ubuntu/Release, 35619...78245c8 macOS/Debug | **fixed** — group 6 |
  | `MemoryReleaseTests.A_released_stopped_row_can_be_retried_to_completion` — NRE on retry (`progress=100%, downloaded=0/65536, attempt=2`) | 35618287475, macOS/Release | **live, undiagnosed** |
  | the two new content-type probe tests losing `UrlResolver`'s 3-slot gate | 35615966666, 35618287475 | fixed, `187c906` |
  Of those, the first two are the ones that mattered; only `MemoryReleaseTests` is still undiagnosed, and
  it has been seen once. Count the five from the first push that carries the group 6 fix. Do not check
  this box by re-running a red one until it goes green: a re-run is exactly what "no re-runs" forbids.
- [x] 5.2 Done (`78245c8`). The superseded section is replaced with what was measured: the two defects
  kept apart (the power-off chain that caused the "hang", the scheduler leak that did not), the detail
  that answered it (ubuntu never hung because its runner refuses a power-off), the guard rails
  (`RealProcessStartBlocked`, the in-step deadline and how to prove it), and how to tell an ordinary red
  leg from that signature.
- [ ] 5.3 `/opsx:sync` the `resource-management` delta, then `/opsx:archive` this change.
  **Deliberately not done: this change is not finished.** 4.4 turned up a live cause and 5.1 has never
  been met, so archiving now would file a solved case over a fault that is still failing CI. The change
  stays active.
