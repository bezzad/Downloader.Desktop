## Context

`DownloadsView` uses a `DataGrid` with `SelectionMode="Extended"`. Its `SelectionChanged` pushes
`grid.SelectedItems` into `DownloadsViewModel.SetGridSelection`, and `SelectedTargets()` =
`Items.Where(i => (i.IsChecked || _gridSelection.Contains(i)) && PassesView(i))`. The row checkbox is
bound to `DownloadItemViewModel.IsChecked`; the header tri-state `SelectAllState` sets `IsChecked` on
the visible rows.

## Goals / Non-Goals

**Goals:** one selection state, standard click/Ctrl/Shift behaviour, checkboxes always match.

**Non-Goals:** keyboard-only multi-select changes beyond what the grid already does; persisting the
selection across restarts.

## Decisions

1. **The grid's `SelectedItems` drives click behaviour; `IsChecked` mirrors it.** Plain / Ctrl /
   Shift click semantics already come from the `DataGrid` in Extended mode, so we reuse them instead
   of re-implementing. On `SelectionChanged`, the view sets `IsChecked = true` for added items and
   `false` for removed items.
2. **The checkbox writes back into the grid selection.** A checkbox click toggles `IsChecked`; the view
   listens (row `PropertyChanged`, as the VM already does for `SelectAllState`) and adds/removes that
   item in `grid.SelectedItems`, without clearing the others. Select-all does the same for every
   visible row. The checkbox's own press is marked handled so the row's click does not also run (which
   would clear the other rows).
3. **One re-entry guard** (`_syncing` flag in the view) stops the two directions from looping.
4. **`SelectedTargets()` = rows with `IsChecked && PassesView`.** Because `IsChecked` now always equals
   "selected", the separate `_gridSelection` list and `SetGridSelection` are removed. Less state, one
   rule. `HasSelection`, `SelectedCount`, the toolbar enablement are unchanged in meaning.
5. **Hidden rows drop out.** When the filter changes, the grid removes rows that disappeared from its
   selection and raises `SelectionChanged` → they are unchecked. Matches the existing rule that bulk
   actions reach only visible rows.
6. **The sync logic is testable without real mouse input.** Headless cannot reliably produce a real
   row click (a click reads as hover — see the skill note), so the tests drive `grid.SelectedItems`
   and `IsChecked` directly and assert the other side, plus one headless Ctrl-modifier click attempt
   if it proves reliable.

## Risks / Trade-offs

- [The grid re-applies selection when the collection view refreshes/sorts] → the sync handles adds and
  removes symmetrically; a test sorts the grid with two rows selected and asserts both stay checked.
- [Drag-to-reorder moves an item in the master list] → a moved row may lose grid selection; accept or
  re-select it after the move (test covers it).
- [Behaviour change] a user used to "check a box, then click another row, both act" now gets only the
  clicked row → that is exactly the author's requested behaviour; Ctrl+click is the way to add.
