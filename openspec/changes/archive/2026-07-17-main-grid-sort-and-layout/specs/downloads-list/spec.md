# downloads-list Specification (delta)

## ADDED Requirements

### Requirement: Column sorting is tri-state and drag-friendly
Clicking a sortable column header SHALL cycle its sort through Ascending, Descending, then None (no sort). In the None state the grid SHALL show items in their master (manual/priority) order and drag-to-reorder SHALL be enabled. When the user starts dragging a row while a sort is active, the sort SHALL be cleared to None (preserving the current visual order) so the drop reorders from there.

#### Scenario: Header click cycles three states
- **WHEN** the user clicks the same column header three times
- **THEN** the sort goes Ascending, then Descending, then None (master order restored)

#### Scenario: Dragging clears an active sort
- **WHEN** a column sort is active and the user begins dragging a row to reorder it
- **THEN** the sort is cleared to None, the visible order is preserved, and the drop reorders the item in master order (which persists)

### Requirement: Select-all checkbox aligns over the row checkboxes
The header select-all checkbox SHALL be horizontally aligned with the per-row selection checkboxes so the selection column reads as a single aligned column.

#### Scenario: Header checkbox sits above the row checkboxes
- **WHEN** the downloads grid is shown with rows
- **THEN** the select-all checkbox is centered over the per-row checkbox column (no visible horizontal offset)
