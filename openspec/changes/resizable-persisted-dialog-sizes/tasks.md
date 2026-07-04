## 1. Config model
- [ ] 1.1 Add a small persisted `WindowSize { double Width; double Height; }` POCO and a `Dictionary<string, WindowSize> WindowSizes` on `Config` (default empty).

## 2. Make Add Download resizable
- [ ] 2.1 `AddDownloadItemView.axaml`: flip `CanResize="True"`, add `MinWidth`/`MinHeight` (e.g. 480×360), keep 560×460 as the shipped default.

## 3. Shared persistence helper
- [ ] 3.1 Add `DialogHelper.ApplyPersistedSize(Window view, string key, Config config)` — reads `config.WindowSizes[key]` if present, clamps to the window's `MinWidth`/`MinHeight` and the owner window's screen working area, and sets `view.Width`/`view.Height` before show.
- [ ] 3.2 Add `DialogHelper.SavePersistedSize(Window view, string key, Config config)` — wired to the window's `Closing` (or `Closed`) event, writes the final `Width`/`Height` into `config.WindowSizes[key]` and triggers the existing debounced save path.
- [ ] 3.3 Wire both helpers around all three dialog show paths (`ShowDialog<TV,TVm,TResult>` for Add Download, `ShowDetails`, `ShowPage`), using distinct keys (`"AddDownload"`, `"Details"`, `"PageDialog"`).

## 4. Tests
- [ ] 4.1 Unit test: `ApplyPersistedSize` clamps a stored size below the window's minimum up to the minimum.
- [ ] 4.2 Unit test: `ApplyPersistedSize` clamps a stored size above the current screen's working area down to it.
- [ ] 4.3 Unit test: `SavePersistedSize` writes the window's current size into `Config.WindowSizes[key]` under the right key.
- [ ] 4.4 Unit test: `PageDialogView` opened for Settings vs Queues restores the same shared size (single key).

## 5. Wrap-up
- [ ] 5.1 Manually resize each of the three dialogs, close and reopen the app, and confirm each restores its own size correctly.
- [ ] 5.2 Refresh `docs/screenshots/` only if a capture happens to show one of these dialogs at a non-default size; otherwise note none was needed.
