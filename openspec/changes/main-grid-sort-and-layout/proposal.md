# Tri-state column sorting (drag-friendly) + header checkbox alignment

## Why

1. **Sorting blocks manual reordering.** Clicking a column header sorts the grid Asc/Desc, but there is no way to clear the sort. Drag-to-reorder rewrites the master `Items` order (queue priority), which only makes sense with no active sort — so with a sort applied, dragging fights the sort and the user's manual order can't stick.
2. **Header checkbox misaligned.** The overlaid select-all checkbox sits a few pixels to the right of the per-row checkboxes, so the column doesn't read as a single aligned column.

## What Changes

- **Tri-state header sorting**: each sortable column cycles Ascending → Descending → **None** (unsorted, natural master order). In the None state the grid shows items in master order and drag-to-reorder works. **Starting a drag while a sort is active auto-clears the sort** to None (keeping the current visual order) so the drop reorders from there.
- **Align the select-all checkbox** exactly above the per-row selection checkboxes (single visual column).

## Capabilities

### Modified Capabilities
- `downloads-list`: 3-state column sort (Asc/Desc/None) with drag-to-reorder enabled only in None; drag auto-clears an active sort; header checkbox aligned over the row checkboxes.

## Impact

- `ViewModels/DownloadsViewModel.cs` (own the sort state on the `DataGridCollectionView` — apply/clear `SortDescriptions`; expose current sort column+direction; a `ClearSort()` used by the drag start).
- `Views/DownloadsView.axaml(.cs)` (intercept header clicks to cycle 3 states instead of the DataGrid's built-in 2-state sort; call `ClearSort()` on drag `PointerPressed`; adjust the overlaid checkbox `Margin` to align with the row checkbox column).
- Tests: header click cycles Asc→Desc→None; drag start clears the sort; alignment is a headless screenshot check.
