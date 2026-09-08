# Tasks — fix-ci-test-host-hang

## 1. Make a hang leave evidence (lands first, independent of any root cause)

- [ ] 1.1 `.github/workflows/dotnet-desktop.yml`: run the `Test` command under `shell: bash` +
  `timeout -k 30 <deadline>` so a stall fails the STEP (exit 124) instead of letting the job be
  cancelled; keep the step `timeout-minutes` and the job timeout as outer nets. Comment why the step
  bound alone is not trusted (it did not fire on 2026-09-08: `Test` ran 32 min under a 12-min bound).
- [ ] 1.2 On the timeout path, before teardown, collect a managed dump of the live `testhost` into
  `./TestResults` (install `dotnet-dump` on demand; find the pid without shelling out to anything
  PATH-hijackable). The existing `if: always()` upload then carries it.
- [ ] 1.3 Prove the harness works: push with a deliberately over-short deadline once, confirm on
  windows-latest AND macos-latest that the step fails at that deadline, the log survives, and the
  artifact contains the dump. Then restore the real deadline. (Git Bash `timeout` on Windows is the
  assumption being tested here.)

## 2. Measure the leaked-scheduler-timer hypothesis (before touching production code)

- [ ] 2.1 Instrument locally (not committed): count `DownloadManager.OnSchedulerTick` invocations and
  live timer instances, and log the totals near the end of a full run.
- [ ] 2.2 Run the full suite on this box with the exact CI command — `--settings src/coverlet.runsettings
  --blame-hang --blame-hang-timeout 180s --blame-crash`, bounded by `timeout -k 30 900`, under
  `taskset -c 0,1`, with no other `dotnet test` live in the tree (`ps` first). Record whether the timer
  count grows with test count.
- [ ] 2.3 Write the measured result into the change (`design.md` → Open Questions) whichever way it goes;
  a disproved hypothesis is a result and must not be silently dropped.

## 3. Stop leaking the scheduler timer

- [ ] 3.1 Failing-first test: many managers initialized+disposed within one dispatcher leave no live
  scheduler timers, and an empty schedule list starts none. It MUST fail on the current code.
- [ ] 3.2 `Services/DownloadManager.cs`: no scheduler timer while `Config.Schedules` is empty; start it
  when the first schedule appears; stop + release it on dispose (manager becomes `IDisposable` if it is
  not already).
- [ ] 3.3 Keep the scheduler working: the existing scheduler tests stay green, and add/keep coverage for
  a schedule added after an empty start actually firing.
- [ ] 3.4 Dispose the managers the test project builds where it holds them (the 133 `Initialize` call
  sites), so the suite stops accumulating anything else that hangs off a manager.
- [ ] 3.5 Full solution build with `-t:Rebuild` → **0 warnings**; full `dotnet test` green locally.

## 4. Fix whatever the evidence names (if the timer was not it, or not all of it)

- [ ] 4.1 On the next captured dump, analyse it with `analyze-hang-dump.yml` on the OS that wrote it
  (`-c pstacks`; in-flight tests are the `Completed="False"` rows in the blame `Sequence_*.xml`), and
  record the finding in `design.md`.
- [ ] 4.2 Fix the named cause with a test that fails on the old code (standing rule: a test that passes
  while the bug survives is a broken test).

## 5. Prove it, then write it down

- [ ] 5.1 Five consecutive `.NET Desktop` runs on `develop` with all 6 legs green and **no re-runs**
  (the failure rate is ~1 leg in 6, so one green run proves nothing).
- [ ] 5.2 Replace the SKILL.md section "The hang got WORSE after PerAssembly, and every test leaks a
  scheduler timer (open)" with what was actually measured and fixed — including the evidence harness, so
  the next session starts from a readable artifact instead of a destroyed log.
- [ ] 5.3 `/opsx:sync` the `resource-management` delta, then `/opsx:archive` this change.
