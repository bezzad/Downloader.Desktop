## 1. Config model
- [x] 1.1 Add a small persisted `WindowSize { double Width; double Height; }` POCO and a `Dictionary<string, WindowSize> WindowSizes` on `Config` (default empty).

## 2. Make Add Download resizable
- [x] 2.1 `AddDownloadItemView.axaml`: flip `CanResize="True"`, add `MinWidth`/`MinHeight` (480×360), keep 560×460 as the shipped default.
- [x] 2.2 **(reprocess — author-reported bug)** `CanResize="True"` alone does NOT enable edge-drag on these windows: they use `WindowDecorations="None"` (custom transparent rounded chrome), which removes the OS resize border, so only maximize/restore worked. The repo already has a working manual-resize overlay (`Views/ResizeGrips`, uses pointer capture + `Window.Position/Width/Height`, deliberately not `BeginResizeDrag` which is a macOS no-op for borderless windows) — but it was only wired into `MainWindow` and `DownloadDetailsView`. Added `<v:ResizeGrips />` as the last child of a wrapping `<Panel>` in **`AddDownloadItemView`** and **`PageDialogView`** (Settings/Queues/Scheduler), matching the two working windows. Now all resizable dialogs can be edge/corner-dragged.

## 3. Shared persistence helper
- [x] 3.1 Add `DialogHelper.ApplyPersistedSize(Window view, string key, Config config)` — reads `config.WindowSizes[key]` if present, clamps to the window's `MinWidth`/`MinHeight` and the window's own screen working area (`view.Screens`, not the main window's — works before the dialog is shown/owned), and sets `view.Width`/`view.Height` before show.
- [x] 3.2 Add `DialogHelper.SavePersistedSize(Window view, string key, Config config)` — wired to the window's `Closing` event, writes the final `Width`/`Height` into `config.WindowSizes[key]`. No explicit save call: the mutated `Config` is the same in-memory instance the app already autosaves every 20s (`MainViewModel`'s periodic timer) and on shutdown, so no new save mechanism was needed.
- [x] 3.3 Wired both helpers around all three dialog show paths (`ShowDialog<TV,TVm,TResult>` for Add Download, `ShowDetails`, `ShowPage`), using distinct keys (`AddDownload`, `Details`, `PageDialog`). Threading `Config` to each call site needed two small additions: `IDownloadManager.Config` (the manager already holds the shared instance) and `DownloadsViewModel.Config` (forwards to the manager) so `DownloadsView.axaml.cs`'s `ShowDetails` call site — which had no direct `Config` reference — can reach it without a new dependency.

## 4. Tests
- [x] 4.1 Unit test: `ApplyPersistedSize` clamps a stored size below the window's minimum up to the minimum. (`DialogHelperTests.ApplyPersistedSize_clamps_below_minimum_up_to_minimum`)
- [x] 4.2 Unit test: `ApplyPersistedSize` clamps a stored size above the current screen's working area down to it. (`ApplyPersistedSize_clamps_above_screen_working_area_down_to_it` — confirms headless `Window.Screens` is available pre-Show)
- [x] 4.3 Unit test: `SavePersistedSize` writes the window's current size into `Config.WindowSizes[key]` under the right key. (`SavePersistedSize_writes_current_size_under_the_given_key`)
- [x] 4.4 Unit test: `PageDialogView` opened for Settings vs Queues restores the same shared size (single key). (`PageDialog_key_is_shared_across_settings_and_queues`) Plus a no-op-when-nothing-saved case. New file `Downloader.Desktop.Tests/DialogHelperTests.cs`, 5 tests, all green (179/179 full suite).

## 5. Wrap-up
- [ ] 5.1 **Awaiting author re-test.** First pass shipped without edge-drag working (only maximize/restore) — the author confirmed the bug on their machine. Fixed in 2.2 by wiring the existing `ResizeGrips` overlay into the two dialogs that lacked it. Needs the author to re-drag the edges of Add-link / Settings / Queues / Scheduler and confirm before this change is done. (Headless environment can't exercise real edge-drag; unit tests cover the size persistence, not the OS drag.)
- [x] 5.2 No existing capture test opens any of these three dialogs at a non-default size — `docs/screenshots/` unaffected, no refresh needed.
