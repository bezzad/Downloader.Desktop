## 1. Grid columns

- [x] 1.1 Remove the Type column; add the category `PathIcon` (color + tooltip) before the name in the Name cell
- [x] 1.2 Remove the per-row action column and the Name-cell `ContextMenu`
- [x] 1.3 Fix the overlaid select-all checkbox margin if the column shift moves it
- [x] 1.4 Test: the grid has no Type column and no action column; the Name cell holds the icon with the category tooltip

## 2. Manager: Restart

- [x] 2.1 `IDownloadManager.Restart(vm)` + implementation (stop if active → delete only `<final>.download` → reset progress/plan → re-queue via `PumpQueue`; refuse Completed)
- [x] 2.2 Test: a stopped row at 40 % restarts at 0 %, partial file gone, completed file untouched
- [x] 2.3 Test: restart respects the queue cap; restart of a Completed row is a no-op

## 3. Copy formats

- [x] 3.1 Pure helper: link / JSON / cURL for a list of `DownloadItem`s (no cookies, headers, cookie file, plan)
- [x] 3.2 Tests: POSIX quoting (quote, space, unicode), referer present/absent, multi-row output, JSON fields, secrets absent

## 4. Menu on the selection

- [x] 4.1 `DownloadsViewModel` commands + canExecute over `SelectedTargets()`: Open, Open folder, Resume(-or-retry), Pause, Stop, Restart, Move-to-queue targets (ObservableCollection rebuilt in place on `QueuesChanged`), Category targets, Copy link/JSON/cURL, Archive/Restore, Delete (= Remove), Properties (single row)
- [x] 4.2 One `ContextMenu` on the grid rows, items in the spec order with separators and icons; no title
- [x] 4.3 Right-button press on an unselected row selects only that row
- [x] 4.4 `InputGesture` labels + grid `KeyBinding`s (Ctrl/Cmd+O, R, P, C, I, Delete)
- [x] 4.5 Tests: enabled rules per state mix; Resume retries a failed row; Properties disabled for 2 rows; Move to queue hidden with one queue; menu acts on all selected rows; right-click on unselected row re-selects
- [x] 4.6 Test: Ctrl+C in the search box does not copy a download link; Delete on the grid removes the selection

## 5. Text

- [x] 5.1 New keys (Restart, Move to queue, Copy, Copy as JSON, Copy as cURL, Properties, Delete if missing) in `en.json` then all 16 packs with real translations

## 6. Finish

- [x] 6.1 Full solution `-t:Rebuild` → 0 warnings; `dotnet test` green
- [x] 6.2 Regenerate `docs/screenshots/` (grid light + dark) and add a capture with the right-click menu open (Copy submenu expanded); view every changed PNG before committing
- [x] 6.3 Skill note for any gotcha (e.g. capturing a popup headlessly); commit + push to `develop`
