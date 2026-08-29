# Raise test coverage above 80%

## Why

app.codecov.io reports **55%** line coverage. The author wants it above 80% (90% ideal), with real
unit and integration tests across every section of the code — not a metric trick.

A baseline run on `develop` (525 tests, all green) collected locally with
`--collect:"XPlat Code Coverage"` measures **51.5%** raw. Breaking that number apart changes the
picture and the plan:

| bucket | covered / total | rate |
|---|---|---|
| real C# code | 5841 / 9543 | **61.2%** |
| generated code (`obj/**/*.g.cs`, regex source generators) | 161 / 2147 | 7.5% |
| compiled XAML (`Views/*.axaml`) | 867 / 1656 | 52.4% |
| **raw total** | 6869 / 13346 | **51.5%** |

Two distinct problems are mixed together:

1. **The denominator is polluted.** 2147 lines are Roslyn *source-generator output* — chiefly a
   1958-line `RegexGenerator.g.cs` DFA in the Website plugin. That code is not authored here and
   cannot be meaningfully unit-tested; it costs ~10 percentage points on its own. Excluding
   generated code is standard practice (coverlet ships `ExcludeByAttribute`/`Exclude` for exactly
   this) and is a correctness fix to the measurement, not a way to dodge writing tests.

2. **Real coverage is being LOST in the full run.** Every file in the Website plugin
   (`LinkExtractor`, `LocalPathMapper`, `SiteCrawler`, `WebsiteTransfer`, `WebsiteResolver` —
   424 lines) reports **0%**, even though `Plugins/Website/WebsiteUnitTests.cs` and
   `WebsiteCrawlTests.cs` exercise them heavily and pass. Running *only* those tests with coverage
   reports the same code as covered (its generated regex jumps 0% → 57%). So the hits exist and are
   discarded somewhere in the full run.

   Bisecting this pinned it as **intermittent, not test-specific**: the same filter
   (`WebsiteUnitTests` + all `Plugins.Hls` tests, 113 tests) reported the Website plugin at 0% on one
   run and fully covered on the next, with no code change in between. Every smaller subset was
   clean. That is a race in coverlet's per-module hit flushing, not something a test can be blamed
   for or a repo change can fix; it can in principle drop any assembly's data on any run. It is
   recorded here so a future session does not re-bisect it, and so a surprising 0% on a
   well-tested file is re-run before being treated as a real gap.

Once the measurement is honest, the gap to 80% is closed by writing tests against the genuinely
untested logic, which the baseline localises precisely.

## What changes

- Add a coverage run configuration that excludes generated output and test-support code from the
  measurement, and fix the Website-plugin hit-loss so the reported number reflects reality.
- Add unit and integration tests to the sections the baseline shows as thin, in descending order of
  uncovered lines. The largest untested areas are view models (`SettingViewModel` 30%,
  `MainViewModel` 35%, `DownloadDetailsViewModel` 49%, `PluginsViewModel` 42%) and services that are
  at or near zero (`UpdateFlow` 2%, `UpdateService` 22%, `DialogHelper` 24%, `CliRunner`,
  `StartupService`, `ShutdownService`, `FileService`, and the small dialog view models).
- Keep every new test hermetic: no live network, no OS side effects, using the existing seams
  (`ShutdownService.PowerOffOverride`, `DownloadManager.RaiseCompletedForTest`, loopback
  `HttpListener`, temp plugin roots) rather than adding new production abstractions.

## Impact

- `src/Downloader.Desktop.Tests/**` — many new test files, foldered by the existing convention
  (`Unit/`, `Integration/`, `UI/`, `Plugins/`).
- `src/coverlet.runsettings` (new) + the CI workflow's `dotnet test` invocation.
- Production code is touched only where a genuine defect is found or a narrow, test-only seam is
  unavoidable; behaviour changes are called out per task.

## Non-goals

- Platform-specific paths that cannot execute on the Linux CI box (`WindowsNotifier`,
  `StartMenuShortcut`'s COM path, the macOS notifier, the update self-swap) stay verified only by
  their pure parts. Chasing them with mocks would add fragile tests, not confidence.
