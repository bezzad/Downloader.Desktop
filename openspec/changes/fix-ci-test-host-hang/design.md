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

## Open Questions

- Why did the step's `timeout-minutes: 12` not fire? Worth understanding (it affects every other step's
  bound), but the change does not depend on the answer — decision 1 does not trust it either way.
- Is the stall before the first test or after the last? The captured dump answers this; the previous
  session could only guess from the absence of `[FAIL]`.
