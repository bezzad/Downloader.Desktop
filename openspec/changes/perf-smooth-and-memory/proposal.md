# Smooth UI under load + release memory on completion

## Why

Two load problems reported from real 2k-download sessions:

1. **Add-link modal freezes ~10 s** when ~2000 links are pasted. Work runs on the UI thread (name/size pre-resolve, per-URL parsing/validation, variant lookup), so the whole app locks up before the modal becomes responsive. Sample input: `/home/bezzad/Downloads/Telegram Desktop/downlod links sample (2).txt`.
2. **Memory never comes back.** With ~2000 items where 99% finished and only 1 was still downloading, app RAM was **~6 GB**; a restart of the same session sat at ~400 MB. Completed downloads' `DownloadService`/`DownloadPackage`/engine buffers are not disposed, so finished rows keep large buffers (incl. `MaximumMemoryBufferBytes`, default 2 GB) alive.

## What Changes

- **Add modal is fully async & non-blocking**: parsing/validating a large pasted list and all per-URL resolve work move off the UI thread; the modal opens and accepts input immediately, resolving names/sizes/variants in the background with cancellation. Pasting 2000 links must not block the UI thread measurably.
- **Dispose the engine on terminal state**: when a download reaches Completed/Failed/Stopped, its `DownloadService` is disposed and its `DownloadPackage`/buffers released; the row keeps only the lightweight persisted `DownloadItem` (name/size/path/status/progress) needed for display and resume. RAM after N completions must return toward the idle baseline, not grow monotonically.

## Capabilities

### New Capabilities
- `resource-management`: engine instances are released on terminal state so memory does not grow unboundedly with completed downloads.

### Modified Capabilities
- `add-download`: adding many links never blocks the UI thread; the modal is responsive during bulk paste.

## Impact

- `ViewModels/AddDownloadItemViewModel.cs` (off-thread parse/validate/resolve, cancellation, batched UI updates), `Views/AddDownloadItemView.axaml(.cs)`.
- `Services/DownloadManager.cs` (dispose engine + null out `vm.Download`/`Package` on terminal transition; guard pause/resume/retry that a disposed row rebuilds its engine), `ViewModels/DownloadItemViewModel.cs` (release engine handle, keep display fields).
- Tests: async-paste responsiveness (no UI-thread block over a large list), and an integration test that downloads many small loopback files and asserts memory returns toward baseline (not strictly growing).
