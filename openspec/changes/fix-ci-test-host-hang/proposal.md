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
