# Stopped/Paused filter + total downloaded size in the status bar

## Why

1. **Users lose their paused downloads.** On restart, interrupted `Running`/`Paused` items are normalized to `Stopped` (a live connection can't survive a restart). The footer filter buckets are All / Active (Running+Paused) / Queued / Completed / Failed — and `Failed` currently also holds `Stopped`. There is no way to see *only* the stopped/paused items, so a user who paused downloads before closing the app can't find them again.
2. **No total-downloaded figure.** The status bar shows total speed but not how many bytes have been downloaded across the session. With 2000 files a user wants to see the cumulative downloaded size.

## What Changes

- **Add a Stopped filter** (Stopped + Paused) to the footer status pills, with its own count, disjoint from the other buckets. `Failed` narrows to Failed-only. Selecting it lists exactly the paused/stopped items so they're never lost after a restart.
- **Show total downloaded size** next to the total speed in the main-window status bar (sum of `Downloaded` across all items, human-readable), updated with the stats pump.

## Capabilities

### Modified Capabilities
- `downloads-list`: new Stopped/Paused footer filter (disjoint buckets), and a cumulative total-downloaded-size readout in the status bar.

## Impact

- `ViewModels/DownloadsViewModel.cs` (new `StatusFilter.Stopped` bucket + `Matches` case; `Failed` = Failed only), `ViewModels/MainViewModel.cs` (`ShowStoppedCommand`/`IsStoppedSelected`/`StoppedFilterCount`; `TotalDownloadedText` off the stats pump).
- `Views/MainWindow.axaml` (new filter pill button; total-downloaded text beside the speed).
- i18n key(s) for the pill label + total-downloaded label in all 16 packs.
- Tests: filter buckets are disjoint and cover every status; the Stopped filter lists paused+stopped; total-downloaded sums correctly.
