## Why

CI legs hang in the `Test` step and are killed by the job timeout. It is not rare and it is not new:
four consecutive `develop` runs on 2026-09-07 lost 1, 2, 3 and 3 legs, and the v2.12.0 release (today)
needed three hand-driven re-runs before `develop` and `main` were both green — two legs hung on `main`
(`windows/Debug`, `macos/Release`) and two on `develop` (`windows/Debug`, `macos/Debug`), and every one of
them passed on re-run with no code change. Ubuntu has never hung.

Two things make this worse than a slow test:

1. **Nobody can see it.** A job cancelled by the job timeout keeps neither its log nor its artifacts —
   verified again today: the hung leg's log is already a `BlobNotFound`. Every occurrence destroys the
   only evidence of itself, which is why this has survived two rounds of fixes.
2. **The bound that was supposed to fix that did not fire.** `d1598f4` put `timeout-minutes: 12` on the
   `Test` step precisely so the step would fail (keeping log + artifacts) before the job was cancelled.
   Today's hung leg started `Test` at 15:12:09 and died at 15:44:56 — 32 minutes — killed by the job's
   30-minute timeout plus the runner's 5-minute grace. The step bound was in the workflow at that commit
   and did not stop it.

There is also a specific, cheap, written-down hypothesis for the hang itself that has never been checked
because the machine that wrote it had no .NET SDK: `DownloadManager.Initialize` calls `StartScheduler()`,
which starts a 30-second `DispatcherTimer` that nothing ever stops (there is no `StopScheduler`, no
`Dispose`). Under the old per-test isolation each test got a fresh dispatcher, so those timers died with
it; since `AvaloniaTestIsolationLevel.PerAssembly` landed (`f1c94d2`) **one** dispatcher serves the whole
run, and the test project calls `Initialize` in 133 places — so a full run ends with 100+ live timers all
ticking `EvaluateSchedules` on the dispatcher the tests themselves need. The hang rate went **up** right
after PerAssembly landed, which fits. This box has the SDK (10.0.111), so it can be measured instead of
guessed.

## What Changes

- **Make a hang always leave evidence.** Bound the test command itself (not only the step) so it dies at a
  known deadline with the step *failing* rather than the job being *cancelled*, and capture the stuck
  test host's stacks (a dump / `pstacks`-analysable artifact) at that deadline, before anything is killed.
  Upload both on failure. This is the part that must land first — without it the next occurrence is
  unreadable again.
- **Check the leaked-scheduler-timer hypothesis with a measurement**, locally, with the exact CI command
  (coverlet runsettings + `--blame-hang`) — count live scheduler timers / `EvaluateSchedules` ticks near
  the end of a full run.
- **Stop leaking the scheduler timer.** The scheduler SHALL not run when there is nothing to schedule and
  SHALL be stoppable: no timer while `Config.Schedules` is empty (which also spares every real user a
  pointless 30-second wake-up), started when a schedule appears, and stopped/released on dispose. Pinned
  by a test that fails on the current code.
- **Fix whatever the captured evidence names**, if it is something else, and pin that too.
- **Prove it with repetition, not with one green run**: the release-gating CI must be green on
  `windows-latest` and `macos-latest`, Debug and Release, over consecutive runs — a hang that shows up in
  1 leg of 6 is not disproved by a single pass.

Non-goal: reverting `PerAssembly`. It is the reason the suite has one app instead of ~1700, and the
earlier "it segfaults" claim was already retracted as a mismeasurement. If the evidence says PerAssembly
is the cause rather than the amplifier, that is a decision to bring back with data.

## Capabilities

### New Capabilities
_None._

### Modified Capabilities
- `resource-management`: the scheduler's periodic timer must not run when there is nothing scheduled, and
  must be released with the manager — a leaked timer is exactly the unbounded-resource class this
  capability already covers.

## Impact

- `.github/workflows/dotnet-desktop.yml` — bounded test invocation, hang capture, artifact upload.
- `src/Downloader.Desktop/Services/DownloadManager.cs` — `StartScheduler`/scheduler lifetime (start on
  demand, stop/dispose); the schedules collection gains a "a schedule appeared" hook.
- `src/Downloader.Desktop.Tests/` — a regression test for the scheduler lifetime; whatever the evidence
  names.
- `.claude/skills/downloader-desktop/SKILL.md` — replace the open "hang got WORSE after PerAssembly"
  note with what was measured.

## Outcome — archived 2026-09-22 with one cause still open

Archived at the author's instruction with **16 of 18 tasks done**. The two unchecked boxes are left
unchecked deliberately, per the repo's rule that an abandoned task keeps its box and states its
reason, so nothing here is filed as solved that is not.

**What this change actually fixed** (all shipped, all pinned by tests):

| fault | status |
|---|---|
| a cancelled shutdown countdown reaching `systemctl poweroff` — it was switching CI runners OFF, and shutting real users' machines down 30 s after they cancelled from the tray | **fixed** (group 4) |
| the scheduler timer leaking from every `Initialize` — 405 live timers per run → 39 | **fixed** (group 3) |
| a hang leaving no evidence: the run is now bounded from inside the step, so a stall fails the STEP with the log intact and a dump of the stuck host | **fixed** (group 1) |
| `MemoryReleaseTests` NRE on retry-after-stop | **fixed** — it was an ENGINE bug, not ours; `Downloader` 5.9.8, taken in `5f91c77` |

**What is still live, and where to pick it up:** the dispatcher binding race (4.4 / group 6).
`Dispatcher.UIThread` is a process-global one-shot that binds to whichever thread touches it first, so
when a plain `[Fact]` reaching dispatcher-touching production code runs before the first
`[AvaloniaFact]`, the session's own `EnsureSharedApplication()` fails its thread-affinity check, the
dispatch loop faults, and every later test waits on a completion source nothing will set. Signature:
`Test Run Aborted` / `Total tests: Unknown` / a hangdump / **no `[FAIL]` anywhere**. Last seen on
`3cff6d0` (run 35689716975, windows/Release) — a docs-only commit, which is the proof it is not
anyone's code.

It is measured and deterministically reproducible (240 s hang vs a 173 ms control, two arms in
separate processes). The obvious fix is known-harmful: `HeadlessSessionFirst` cleared every local
signal and took CI from ~1 leg in 6 to 4 in 6, and was reverted in `389f71b`. **A green local suite
does not clear a change to the headless session's lifetime** — that is the most valuable thing this
change learned. A next attempt should measure on CI (the `workflow_dispatch` deadline inputs are a
cheap harness) and aim at making the first dispatcher touch land on the session thread WITHOUT
taking the session up early.

Because the fault is a test-harness one, it costs a re-run rather than shipping a defect; task 5.1's
five-green-runs bar is therefore a quality gate that was never met, not a regression. Reopen this as
a new, narrower change when it is worth another attempt.
