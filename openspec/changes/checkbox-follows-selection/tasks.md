## 1. One selection state

- [x] 1.1 `DownloadsView`: on `SelectionChanged`, set `IsChecked` true/false for added/removed rows (guarded by `_syncing`)
- [x] 1.2 `DownloadsView`: when a row's `IsChecked` changes (checkbox or select-all), add/remove it in `grid.SelectedItems` without clearing others (guarded)
- [x] 1.3 Mark the checkbox press handled so a checkbox click does not also run the row click — no code needed: the CheckBox already marks its press handled
- [x] 1.4 `DownloadsViewModel`: `SelectedTargets()` = `IsChecked && PassesView`; remove `_gridSelection` / `SetGridSelection`
- [x] 1.5 Confirm `IsChecked` is not persisted (keep it transient) — confirmed: it lives only on the row VM

## 2. Tests (must fail on the old code)

- [x] 2.1 Selecting row B in the grid after A unchecks A (old code leaves A checked)
- [x] 2.2 Adding B to `SelectedItems` while A is selected keeps both checked
- [x] 2.3 Checking a checkbox adds the row to `SelectedItems` without removing others
- [x] 2.4 Select-all selects every visible row in the grid; clearing it empties the selection
- [x] 2.5 A filter that hides a selected row unchecks it; bulk Pause touches only the checked rows
- [x] 2.6 Sorting with two rows selected keeps both checked; a drag-reorder keeps a sane selection
- [x] 2.7 Update/remove the existing tests that relied on `SetGridSelection` ("checked OR highlighted")

## 3. Finish

- [x] 3.1 Full solution `-t:Rebuild` → 0 warnings; `dotnet test` green
- [x] 3.2 Screenshot: add a capture with two rows selected (programmatic `SelectedItems`), light + dark; view the PNGs before committing
- [x] 3.3 Commit + push to `develop`
