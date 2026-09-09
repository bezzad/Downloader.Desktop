#!/usr/bin/env bash
# Runs the test suite in CI under a deadline that PRESERVES the evidence when it hangs.
#
# Why this exists instead of a plain `dotnet test` step:
#   * A leg that stalls in the test step used to be killed by the JOB timeout, and GitHub keeps neither
#     the log nor the artifacts of a job it cancelled — so every occurrence of the recurring
#     Windows/macOS hang destroyed the only evidence of itself.
#   * The step-level `timeout-minutes` was added to fix exactly that and DID NOT FIRE: on 2026-09-08 a
#     leg's Test step ran 32 minutes under a 12-minute step bound and still died on the job timeout
#     (30 min + the runner's 5-minute force-kill grace). So the deadline has to live inside the step.
#   * `--blame-hang` does not cover this signature either: it watches for a host that stops responding
#     BETWEEN tests, and these stalls happen outside that window (no [FAIL], no abort, no dump).
#
# So: a bash watchdog collects a managed dump of the stuck test host slightly BEFORE the deadline, then
# the deadline kills the run and this script exits non-zero. The step FAILS (log kept) instead of the job
# being cancelled (log lost), and the workflow's `if: always()` upload carries the dump out.
# Analyse it with .github/workflows/analyze-hang-dump.yml on the OS that wrote it — dumps are native.
#
# The bound is implemented in bash, not with `timeout`: GNU coreutils `timeout` is not on macOS runners.
#
# Usage: scripts/ci-test.sh <Debug|Release>
#   CI_TEST_DEADLINE_SECONDS  total budget for the run (default 900)
#   CI_TEST_DUMP_LEAD_SECONDS how long before the deadline to grab the dump (default 120)
set -uo pipefail

CONFIGURATION="${1:?usage: ci-test.sh <Debug|Release>}"
DEADLINE="${CI_TEST_DEADLINE_SECONDS:-900}"
DUMP_LEAD="${CI_TEST_DUMP_LEAD_SECONDS:-120}"
RESULTS="./TestResults"
TEST_PROJECT="src/Downloader.Desktop.Tests/Downloader.Desktop.Tests.csproj"
RUNSETTINGS="src/coverlet.runsettings"

DUMP_AT=$(( DEADLINE > DUMP_LEAD ? DEADLINE - DUMP_LEAD : DEADLINE ))
mkdir -p "$RESULTS"
MARKER="$RESULTS/.test-finished"
# The watchdog leaves this behind when it kills the run, and the script then fails REGARDLESS of the exit
# status it waited for. Proving run 34311757571 showed why that matters: on Linux and macOS a SIGTERM'd
# `dotnet test` exits 0, so the killed legs reported SUCCESS (only Windows surfaced 143) — a harness that
# hides the hang it exists to catch.
KILLED="$RESULTS/.test-killed"
rm -f "$MARKER" "$KILLED"

# Collect a dump of whatever test host is still alive, then let the deadline kill the run.
watchdog() {
  sleep "$DUMP_AT"
  [ -f "$MARKER" ] && return 0

  echo "::error::The test run is still going after ${DUMP_AT}s (deadline ${DEADLINE}s) — capturing a hang dump before it is killed."
  dotnet tool install --global dotnet-dump >/dev/null 2>&1 || true
  dump_tool="$HOME/.dotnet/tools/dotnet-dump"
  [ -x "$dump_tool" ] || dump_tool="$(command -v dotnet-dump || true)"
  if [ -n "$dump_tool" ]; then
    # The test host is NOT called the same thing everywhere: `--name testhost` matched on
    # windows-latest and found nothing on ubuntu/macOS, where the process is plain `dotnet`
    # (run 34311757571: "Attaching crash dump utility to process testhost" vs "…process dotnet").
    # So ask dotnet-dump which .NET processes it can see and pick the test host out of them, by
    # command line, falling back to any dotnet process that is not this script's own child.
    host_pid="$("$dump_tool" ps 2>/dev/null | grep -i "testhost" | awk '{print $1}' | head -1)"
    if [ -z "$host_pid" ]; then
      host_pid="$("$dump_tool" ps 2>/dev/null | awk -v skip="$TEST_PID" '$1 != skip {print $1}' | tail -1)"
    fi

    if [ -n "$host_pid" ]; then
      "$dump_tool" collect --process-id "$host_pid" --output "$RESULTS/hang-testhost.dmp" \
        || echo "::warning::dotnet-dump could not collect a dump of process $host_pid"
    else
      echo "::warning::no .NET process to dump — dotnet-dump ps listed none"
    fi
  else
    echo "::warning::dotnet-dump is unavailable — no hang dump was captured"
  fi

  sleep "$DUMP_LEAD"
  [ -f "$MARKER" ] && return 0
  echo "::error::Killing the test run at its ${DEADLINE}s deadline."
  # Recorded BEFORE the kill: this, not the exit status, is what fails the step.
  : > "$KILLED"
  # Closes the last narrow race: if the run finished in the instant between the check above and this
  # write, withdraw the sentinel rather than failing a run that actually completed.
  if [ -f "$MARKER" ]; then
    rm -f "$KILLED"
    return 0
  fi
  kill -TERM "$TEST_PID" 2>/dev/null || true
  sleep 15
  kill -KILL "$TEST_PID" 2>/dev/null || true
}

dotnet test "$TEST_PROJECT" \
  --configuration "$CONFIGURATION" \
  --no-build \
  --verbosity normal \
  --logger "trx;LogFileName=test-results.trx" \
  --results-directory "$RESULTS" \
  --settings "$RUNSETTINGS" \
  --blame-hang --blame-hang-timeout 180s --blame-crash &
TEST_PID=$!

watchdog &
WATCHDOG_PID=$!

wait "$TEST_PID"
STATUS=$?
touch "$MARKER"
kill "$WATCHDOG_PID" 2>/dev/null || true

if [ -f "$KILLED" ]; then
  # A SIGTERM'd `dotnet test` exits 0 on Linux and macOS, so the wait status cannot be trusted to
  # report this. The sentinel can.
  echo "::error::The test run was KILLED at its ${DEADLINE}s deadline (wait reported $STATUS). The uploaded test-results artifact holds hang-testhost.dmp."
  exit 124
fi

if [ "$STATUS" -ne 0 ]; then
  echo "::error::dotnet test exited $STATUS."
fi
exit "$STATUS"
