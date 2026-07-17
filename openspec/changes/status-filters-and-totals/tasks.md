# Tasks — status-filters-and-totals

Each task is TDD: failing test first, make it pass, keep build + full `dotnet test` green, commit to `develop`, push, confirm GitHub Actions green before the next task.

## 1. Stopped/Paused filter (task #2)

- [x] 1.1 Write tests: a `StatusFilter.Stopped` bucket matches Paused+Stopped; buckets are disjoint and counts sum to total across a mixed list; `Failed` matches Failed only; `Active` matches Running only.
- [x] 1.2 Add the `Stopped` bucket to `DownloadsViewModel.Matches`, narrow `Active`/`Failed`, add `MainViewModel.ShowStoppedCommand`/`IsStoppedSelected`/`StoppedFilterCount` (+ re-raise in stats). Make 1.1 pass.
- [x] 1.3 Add the footer pill in `MainWindow.axaml` + i18n label in all 16 packs. Build + full tests green; regenerate the home screenshots; commit/push; wait for green CI.

## 2. Total downloaded in status bar (task #18)

- [x] 2.1 Write a test: `MainViewModel.TotalDownloadedText` equals the human-readable sum of item `Downloaded` for a set of rows and updates on the stats pump.
- [x] 2.2 Implement `TotalDownloadedText` (sum in the stats-pump handler) + i18n label. Make 2.1 pass.
- [x] 2.3 Place it beside the speed text in `MainWindow.axaml`. Build + full tests green; regenerate home screenshots; commit/push; wait for green CI.
