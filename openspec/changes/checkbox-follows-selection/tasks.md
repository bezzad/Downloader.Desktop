## 1. One selection state

- [ ] 1.1 `DownloadsView`: on `SelectionChanged`, set `IsChecked` true/false for added/removed rows (guarded by `_syncing`)
- [ ] 1.2 `DownloadsView`: when a row's `IsChecked` changes (checkbox or select-all), add/remove it in `grid.SelectedItems` without clearing others (guarded)
- [ ] 1.3 Mark the checkbox press handled so a checkbox click does not also run the row click
- [ ] 1.4 `DownloadsViewModel`: `SelectedTargets()` = `IsChecked && PassesView`; remove `_gridSelection` / `SetGridSelection`
- [ ] 1.5 Confirm `IsChecked` is not persisted (keep it transient)

## 2. Tests (must fail on the old code)

- [ ] 2.1 Selecting row B in the grid after A unchecks A (old code leaves A checked)
- [ ] 2.2 Adding B to `SelectedItems` while A is selected keeps both checked
- [ ] 2.3 Checking a checkbox adds the row to `SelectedItems` without removing others
- [ ] 2.4 Select-all selects every visible row in the grid; clearing it empties the selection
- [ ] 2.5 A filter that hides a selected row unchecks it; bulk Pause touches only the checked rows
- [ ] 2.6 Sorting with two rows selected keeps both checked; a drag-reorder keeps a sane selection
- [ ] 2.7 Update/remove the existing tests that relied on `SetGridSelection` ("checked OR highlighted")

## 3. Finish

- [ ] 3.1 Full solution `-t:Rebuild` → 0 warnings; `dotnet test` green
- [ ] 3.2 Screenshot: add a capture with two rows selected (programmatic `SelectedItems`), light + dark; view the PNGs before committing
- [ ] 3.3 Commit + push to `develop`
