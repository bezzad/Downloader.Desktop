# Smooth UI under load + release memory on completion

## Why

Two load problems reported from real 2k-download sessions:

1. **Add-link modal freezes ~10 s** when ~2000 links are pasted. Diagnosed root cause: it is **not** parsing (splitting the 444 KB sample list is single-digit ms) and it is **not** per-URL network work (the multi-URL path already skips name/size/variant resolution). The cost is Avalonia's multi-line `TextBox` building a `TextLayout` for **all** pasted lines — its `TextPresenter` does not virtualize lines, so laying out 2000+ lines on the UI thread is the freeze. Both the top-bar paste box and the modal's links box hit this. Sample input: `/home/bezzad/Downloads/Telegram Desktop/downlod links sample (2).txt`.
2. **Memory never comes back.** With ~2000 items where 99% finished and only 1 was still downloading, app RAM was **~6 GB**; a restart of the same session sat at ~400 MB. Completed downloads' `DownloadService`/`DownloadPackage`/engine buffers are not disposed, so finished rows keep large buffers (incl. `MaximumMemoryBufferBytes`, default 2 GB) alive.

## What Changes

- **Add modal stays responsive on a huge paste**: a large list is represented as a compact summary ("N links ready to add") instead of being rendered line-by-line in an editable `TextBox`, so no giant `TextLayout` is built. `Ctrl+V`/`Shift+Insert` of a large list is intercepted so the box never lays it out, and a large paste in the top-bar box opens the Add dialog directly in this bulk mode. Small pastes are unchanged (still an editable box). Parsing is also cached (computed once per change, not re-split on every property read).
- **Dispose the engine on terminal state**: when a download reaches Completed/Failed/Stopped, its `DownloadService` is disposed and its `DownloadPackage`/buffers released; the row keeps only the lightweight persisted `DownloadItem` (name/size/path/status/progress) needed for display and resume. RAM after N completions must return toward the idle baseline, not grow monotonically.

## Capabilities

### New Capabilities
- `resource-management`: engine instances are released on terminal state so memory does not grow unboundedly with completed downloads.

### Modified Capabilities
- `add-download`: adding many links never blocks the UI thread; the modal is responsive during bulk paste.

## Impact

- `ViewModels/AddDownloadItemViewModel.cs` (cached parse; `LinkCount`/`IsBulk`/`BulkSummaryText`/`ClearUrls`), `Views/AddDownloadItemView.axaml(.cs)` (hide the editable box + show a summary chip when bulk; intercept large paste), `Views/MainWindow.axaml.cs` + `ViewModels/MainViewModel.cs` (route a large top-bar paste straight into the Add dialog), i18n keys.
- `Services/DownloadManager.cs` (dispose engine + null out `vm.Download`/`Package` on terminal transition; guard pause/resume/retry that a disposed row rebuilds its engine), `ViewModels/DownloadItemViewModel.cs` (release engine handle, keep display fields).
- Tests: async-paste responsiveness (no UI-thread block over a large list), and an integration test that downloads many small loopback files and asserts memory returns toward baseline (not strictly growing).
