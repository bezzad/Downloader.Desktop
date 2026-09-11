## Context

The `.NET Desktop` workflow runs 6 legs (3 OSes × Debug/Release). Intermittently a Windows or macOS leg
sits in the `Test` step until it is killed; ubuntu never has. Two earlier root causes in this same step
were found and fixed and are not this one:

- a parallel-collection race that killed the headless dispatcher → fixed by disabling test parallelization;
- coverlet's module-unload hook resolving a type through an unloading `AssemblyLoadContext` → fixed by
  making an unloading context answer "not mine" (`PluginLoadContextTests` pins it).

What is left has a distinct signature: **no `[FAIL]`, no dump, no abort** — the host simply stops making
progress, which puts the stall outside the `--blame-hang --blame-hang-timeout 180s` window (that watches
for a host that stops responding *between* tests). It began being frequent right after
`AvaloniaTestIsolationLevel.PerAssembly` landed (`f1c94d2`): 1, 2, 3, 3 hung legs across the next four
`develop` runs, and 4 legs across the two runs of today's release.

Three constraints shape the approach:

1. **Evidence is currently destroyed on every occurrence.** GitHub keeps neither log nor artifacts for a
   job it *cancelled* on the job timeout — re-verified today (`BlobNotFound` on the hung leg's log).
2. **The existing step bound does not fire.** `timeout-minutes: 12` was on the `Test` step at the commit
   that hung; that leg still ran 32 minutes and died on the job timeout (30 min) plus the runner's 5-min
   force-kill grace. Whatever the reason, the workflow cannot rely on it.
3. **It does not reproduce on Linux**, and this box is Linux — 19 consecutive full-suite runs with the
   exact CI command were clean. So local work can *measure* the hypothesis (timer counts, tick rates) but
   cannot be the proof that the hang is gone; only repeated green CI on the Windows/macOS legs can.

## Goals / Non-Goals

**Goals:**
- A hang always produces a readable artifact: the step fails at a known deadline with its log intact and a
  managed-stack dump of the stuck host uploaded.
- Confirm or kill the leaked-scheduler-timer hypothesis with a measurement, not an argument.
- Stop leaking the scheduler timer, in production code, with a test that fails on today's code.
- Green Windows/macOS legs (Debug and Release) over consecutive runs.

**Non-Goals:**
- Reverting `PerAssembly` (see proposal). It stays unless evidence names it.
- Making the suite faster, or reducing the 1700-test count.
- Touching the two already-fixed causes above.

## Decisions

### 1. Bound the command, not (only) the step — `timeout` under `shell: bash`

The step-level `timeout-minutes` demonstrably did not stop a hung leg, so the deadline moves *inside* the
step: run the test command under `timeout -k 30 <deadline>` with `shell: bash`, which exists on all three
runners (Git Bash ships coreutils `timeout` on `windows-latest`). A `timeout` exit (124) fails the **step**,
so the log survives and the `if: always()` artifact upload still runs. The step's `timeout-minutes` stays as
a second net, and the job's 30 minutes as a third.

*Alternative considered:* `dotnet test --blame-hang-timeout` tuned lower. Rejected — this stall is outside
the window `--blame-hang` watches at all (that is the whole point of this signature), so a smaller number
changes nothing.

### 2. Capture the stacks at the deadline, before killing anything

`timeout` alone proves *that* it hung, not *where*. On the timeout path the step SHALL, before the run is
torn down, collect a dump of the live `testhost` process (`dotnet-dump collect -p <pid>`, tool installed on
demand) into the results directory that `if: always()` already uploads. Dumps are platform-native, and the
repo already has `analyze-hang-dump.yml` to run `dotnet-dump analyze -c pstacks` **on the OS that wrote the
dump** — so this decision is deliberately "capture here, analyse there" rather than analysing inline.

*Alternative considered:* `dotnet-stack report` (text, no big artifact). Cheaper, but it cannot answer the
questions the last two root causes needed (object state, task flags), and a dump can always be reduced to a
`pstacks` text later. Keep the dump; it is only uploaded on the failure path.

### 3. Verify the timer-leak hypothesis by counting, on this box, before changing production code

The hypothesis is concrete: `DownloadManager.Initialize` → `StartScheduler()` starts a 30-second
`DispatcherTimer` with no `StopScheduler` and no `Dispose`; the test project calls `Initialize` in 133
places; under PerAssembly a single dispatcher serves all ~1700 tests, so the timers accumulate for the
whole run instead of dying with each per-test app. Measurement (Linux, exact CI command incl. coverlet
runsettings): instrument `OnSchedulerTick` to count invocations per manager instance and log the total near
the end of a full run. If the count grows with the number of tests, the hypothesis holds.

This ordering matters because the fix touches production scheduling, and the previous session wrote the
hypothesis down rather than guessing at it precisely because it could not measure. We can.

### 4. The scheduler starts on demand and is disposable

The fix is the smallest one that satisfies the spec and is also right for real users: no timer while there
is nothing to schedule (a fresh install with no schedules currently wakes the dispatcher every 30 s for
nothing), start it when a schedule appears, stop and release it on dispose. The manager becomes
`IDisposable` if it is not already, and the tests dispose the managers they build.

*Alternative considered:* keep the timer always-on but have the tests dispose it. Rejected: it leaves every
real user paying for an idle timer, and 133 call sites would each have to remember the teardown — the
"remember to clean up" shape that produced this bug.

*Alternative considered:* make the timer static/shared. Rejected: a manager-per-test is exactly what the
tests want to isolate; sharing state across them re-creates the parallel-collection class of bug.

### 5. Proof is repetition on the affected legs

A single green run proves nothing when the failure rate is ~1 leg in 6. Acceptance: the Windows and macOS
legs (Debug and Release) green across **five consecutive** `.NET Desktop` runs on `develop` with no
re-runs. If the hang recurs after the timer fix, the captured dump is now available and the change
continues from that evidence rather than from a new guess.

## Risks / Trade-offs

- **The timer leak may be an amplifier, not the cause** → the evidence harness (decisions 1–2) lands first
  and independently, so the next occurrence is readable no matter what the timer measurement says.
- **`timeout` under Git Bash on `windows-latest` could behave differently than assumed** → verify on the
  first CI run that a deliberate over-short deadline produces exit 124 and an uploaded artifact, before
  relying on it (task in `tasks.md`).
- **Lazy-starting the scheduler could stop a schedule from firing** if the "a schedule appeared" hook misses
  a path that adds schedules (config load, Scheduler page, restored config) → the spec's scenarios cover
  add-after-empty, and the existing scheduler tests must keep passing.
- **A dump artifact is large** (hundreds of MB for a hung host) → collected only on the failure path, and
  the results artifact already uses `if-no-files-found: ignore`.
- **Cannot be reproduced locally**, so the fix is validated by CI repetition — slower feedback than usual;
  accepted, and the reason the acceptance bar is five runs rather than one.

## What the investigation actually found (2026-09-09) — supersedes the hypothesis above

**A cancelled shutdown countdown was powering the machine off mid-run.** Found the way the best bugs
are: the author noticed that every time this session was told to continue, the computer shut down about a
minute later, and asked whether one of the agent's commands contained a shutdown. None did — the test
suite did it.

The chain:

1. `ShutdownService.Cancel()` (the tray's "cancel shutdown", and what the suite calls between tests)
   closed the countdown window and cleared its own field, but the countdown is a `DispatcherTimer` that
   lives on the **dispatcher, not the window**. Closing the dialog left it ticking.
2. Under `PerAssembly` the dispatcher lives for the whole run (under the old `PerTest` each test got a
   fresh one and the leaked timer died with it), so the countdown survived the test that armed it,
   reached zero minutes later, and called `PowerOff()`.
3. By then the arming test's `finally` had restored `PowerOffOverride = null` and `RunOverride = null`,
   so `PowerOff()` reached the real platform dispatch: `systemctl poweroff` on Linux,
   `shutdown /s /t 0` on Windows, `osascript … shut down` on macOS.

**This is also the best explanation of the CI hang, and it fits the signature the scheduler-timer
hypothesis never did:** a Windows or macOS runner ACCEPTS a power-off, so the leg stops making progress
with no `[FAIL]`, no dump and no abort, and the job's log and artifacts are destroyed with it — while a
GitHub-hosted ubuntu runner refuses `systemctl poweroff`, which is why ubuntu has never hung. Confirmed
only as far as the mechanism goes; the proof is the five clean runs in tasks 5.1.

**It is also a production bug, and the worse half of it:** a real user who cancelled the shutdown from
the tray had their machine powered off 30 seconds later anyway. The dialog's own Cancel button was safe
(it goes through the view model, which does stop the timer), so the broken path was the one nobody
watches.

Fixes, in order of what each protects:

- `ShutdownService.Close()` stops the countdown (`ShutdownViewModel.StopCountdown()`) BEFORE closing the
  window. Pinned by `UI/ShutdownCancelTests`, which fails on the old code with `'systemctl' should not
  have run`. Two earlier drafts of that test passed against the buggy code — a 3-second pump against a
  30-second countdown, then assertions that removed the very stub that would observe the fire — so the
  countdown length is now a test seam (`ShutdownService.CountdownSeconds`) and the observation happens at
  the launcher, one layer below the override that would mask it.
- `ShellLauncher.RealProcessStartBlocked`, installed once by the test assembly
  (`TestSupport/NoRealPowerOff`, same `[ModuleInitializer]` pattern as `NoRealNotifications`): the suite
  can no longer start a real process at all, so the next leak of this class fails a test instead of
  taking a machine down. `AllowRealProcessStart()` is the explicit opt-out for the three tests in
  `Unit/RevealInFolderTests` that mean to run `/bin/false`, `sleep`, and a missing command — without it
  they would have passed for the wrong reason, which is worse than failing.

**The scheduler timer leak was real too, and is fixed** — just not the whole story. Measured on a full
local run with the CI flags: **405** scheduler timers started per run, ticks arriving in bursts from
dozens of manager instances after their tests had ended. With the fix (start only while
`Config.Schedules` is non-empty; stop and release on dispose) that is **39** — the tests that genuinely
configure a schedule. Pinned by `UI/SchedulerLifetimeTests` (5 of its 6 tests fail on the old code).

Full suite after both fixes: **1728/1728 green**, full rebuild **0 warnings**, no crash or hang dump.

## Open Questions

- Why did the step's `timeout-minutes: 12` not fire? (Answer candidate from the finding above: a runner
  whose OS has begun shutting down is in no state to enforce a step timeout — the runner process is going
  away with everything else. That would explain both the missed step bound and the 35-minute job.)
  Worth understanding either way, since it affects every other step's bound — but the change does not
  depend on the answer: decision 1 does not trust the step bound in either direction.
- Is the stall before the first test or after the last? Moot if the power-off explanation holds (the run
  is not stalled at all, the machine is going down under it). The captured dump answers it if a hang
  recurs after these fixes.
