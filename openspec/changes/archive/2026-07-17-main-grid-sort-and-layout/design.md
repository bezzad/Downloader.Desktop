# Design — main-grid-sort-and-layout

## Context
The grid binds a flat `DataGridCollectionView` (no grouping, for virtualization). Avalonia's DataGrid header click does built-in 2-state sorting (Asc/Desc, never off) by mutating `SortDescriptions`. Drag-to-reorder (the manual pointer-drag ghost) reorders the master `Items` list = pump priority; that only makes sense when the view isn't sorted, otherwise the reordered items snap back under the active sort. The select-all checkbox is an overlay (`Margin="42 9 0 0"`, offset for the 28px drag grip) whose X doesn't line up with the row checkbox cell.

## Goals / Non-Goals
**Goals:** a clear "no sort" state reachable from the UI; drag-reorder that works and survives (in None state); an aligned select-all checkbox.
**Non-Goals:** multi-column sort; persisting sort state across restarts; changing what a drag does (still reorders master `Items`).

## Decisions
1. **Own the sort, disable the built-in cycle.** Set `DataGridColumn.CanUserSort=false` (or handle `Sorting` and cancel it) so the DataGrid doesn't apply its own 2-state sort, and drive sorting from the VM: header click cycles a `(column, SortDir?)` where `SortDir? == null` is None. Apply by setting `CollectionView.SortDescriptions` (Asc/Desc) or clearing them (None → master order). Show the sort glyph from our state.
2. **Drag auto-clears the sort (author-selected default).** On drag `PointerPressed` (grip), if a sort is active call `DownloadsViewModel.ClearSort()` — this drops `SortDescriptions` so the view is back in master order; because the current visual order already equals the sorted order at that instant, clearing is visually stable, and the subsequent `ReorderTo` moves the row within master order. Rejected alternative: block the grip while sorted (extra click, worse UX).
3. **Checkbox alignment.** The row selection checkbox lives in a fixed-width (`Width=44`) column after the grip. Align the overlay by matching its `Margin`/`HorizontalAlignment` to the row checkbox's rendered X (account for grip width + cell padding). Verify with a headless screenshot (`home-*` capture) since a pixel offset isn't unit-testable.

## Risks / Trade-offs
- [Canceling the DataGrid's built-in sort but keeping its header UI] → we render our own glyph/state; if the built-in glyph fights it, hide the built-in sort indicator via style.
- [Auto-clearing sort on drag surprises a user who wanted to keep the sort] → acceptable and matches the request ("when drag drop that disable sorting and keep display as user want"); the rows stay where they visually were.
