## Why

`AddDownloadItemView` is hard-locked at 560×460 (`CanResize="False"`), which becomes cramped with multiple URLs pasted in. `PageDialogView` (Queues/Scheduler/Settings) and `DownloadDetailsView` are already resizable but forget the user's preferred size the moment they're closed — every reopen starts back at the shipped default. Users who prefer a larger details/settings window have to resize it every single time.

## What Changes

- `AddDownloadItemView` becomes resizable (`CanResize="True"`) with sensible `MinWidth`/`MinHeight` (keeping 560×460 as the default/initial size).
- All modal windows (`AddDownloadItemView`, `PageDialogView`, `DownloadDetailsView`) persist their last user-resized `Width`/`Height` and restore it the next time that window type is opened, instead of always reopening at the shipped default.
- Persistence is per **window type**, not per dialog instance — e.g. all `PageDialogView` opens (Queues/Scheduler/Settings share one view type) remember one shared size; `DownloadDetailsView` remembers its own size independent of `AddDownloadItemView`.
- Sizes are stored in `Config` (new small `WindowSizes` section, e.g. a `Dictionary<string, (double Width, double Height)>` keyed by window name) and saved through the existing debounced `FileService` save path — no new save mechanism.
- Persisted sizes are clamped to each window's own `MinWidth`/`MinHeight` (and a reasonable max, e.g. current screen working area) on load, so a corrupted/stale config value or a size saved on a larger monitor can't produce an unusable or off-screen window.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `ui-theme`: modal windows are resizable and remember their last size across sessions, per window type.

## Impact

- `src/Downloader.Desktop/Models/Config.cs` — new persisted `WindowSizes` dictionary.
- `src/Downloader.Desktop/Views/AddDownloadItemView.axaml` — `CanResize="True"` + `MinWidth`/`MinHeight` added.
- `src/Downloader.Desktop/Views/PageDialogView.axaml`, `Views/DownloadDetailsView.axaml` — no XAML resize-flag change (already resizable), but wired to save/restore size.
- `src/Downloader.Desktop/Services/DialogHelper.cs` — `ShowDialog<TV,TVm,TResult>` gains an optional size-persistence hook (read saved size before show, write current size on close), shared across all three dialog types via a window-name key rather than per-dialog duplicated code.
- Existing screenshot capture test (`CaptureScreenshots`) is unaffected since it doesn't resize windows.
