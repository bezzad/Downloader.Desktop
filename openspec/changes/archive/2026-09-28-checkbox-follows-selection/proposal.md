## Why

The downloads grid has two separate "selected" states today: the row checkbox (`IsChecked`) and the
grid's own highlighted rows. Toolbar actions use *checked OR highlighted*, so clicking row A, then row
B, leaves A's checkbox as it was while B is highlighted — the user cannot tell from the checkboxes what
an action will hit. The author wants one state: the checkbox shows exactly which rows are selected, a
plain click selects one row, Ctrl+click adds rows. (Step 2 of 4; the right-click menu in step 3 acts on
this selection.)

## What Changes

- The row checkbox **is** the row's selection — they can never disagree.
- **Click** a row → that row is selected and checked; every other row is unselected and unchecked.
- **Ctrl+click** (Cmd+click on macOS) → toggles that row and keeps the others.
- **Shift+click** → selects the range (the grid's normal extended selection), checkboxes follow.
- Clicking a row's **checkbox** toggles only that row and keeps the others (like Ctrl+click).
- The header **select-all** selects/unselects all visible rows (unchanged meaning; now also highlights them).
- When a filter hides a selected row, it stops being selected (it cannot be acted on anyway — the
  bulk actions already reach only visible rows).
- Double-click still opens the Details window; drag-to-reorder from the grip is unchanged.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `downloads-list`: the checkbox and the highlighted rows become one selection with standard
  click / Ctrl+click / Shift+click behaviour.

## Impact

- `Views/DownloadsView.axaml(.cs)` — two-way sync between `DataGrid.SelectedItems` and each row's
  `IsChecked`, with a re-entry guard.
- `ViewModels/DownloadsViewModel.cs` — `SelectedTargets()` becomes "the selected rows" (the separate
  `_gridSelection` list folds into it); `SelectAllState` keeps working.
- Tests in `UI/` for the sync rules. No i18n, no model or persistence change (`IsChecked` is not
  persisted today — check and keep it that way).
- `docs/screenshots/` — a capture with two rows selected.
