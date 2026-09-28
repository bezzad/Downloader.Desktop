# downloads-list Specification

## Purpose

Presentation of the downloads grid rows in the main window.

## Requirements

### Requirement: Full file name is readable on hover
The downloads grid SHALL let the user read a download's complete file name by hovering the pointer over its Name cell, even when the name is too long to fit the column and is trimmed with an ellipsis.

#### Scenario: Hover reveals the full name
- **WHEN** the pointer hovers over a Name cell whose text is trimmed
- **THEN** a tooltip shows the complete file name

#### Scenario: Failed download also shows its error
- **WHEN** the pointer hovers over the Name cell of a failed download
- **THEN** the tooltip shows the full name and the failure reason

### Requirement: A Stopped/Paused filter lists interrupted downloads
The footer filters SHALL include a Stopped bucket that matches items in the Paused or Stopped state, with a count of exactly those items. The status filter buckets (All, Active, Queued, Completed, Stopped, Failed) SHALL be mutually disjoint and jointly cover every item that is not archived, so a user can always find paused/stopped downloads after a restart. Archived items SHALL be excluded from every status bucket, including All, and SHALL be listed only by the separate Archived filter.

#### Scenario: Paused downloads are visible after restart
- **WHEN** the user paused downloads before closing, and reopens the app (interrupted items load as Stopped)
- **THEN** selecting the Stopped filter lists those paused/stopped items and its count equals their number

#### Scenario: Buckets are disjoint and exhaustive
- **WHEN** the list contains a mix of Running, Paused, Stopped, Queued, Completed and Failed items, none of them archived
- **THEN** each item matches exactly one of the non-All buckets and the bucket counts sum to the total item count

#### Scenario: Archived items are outside the status buckets
- **WHEN** the list contains archived items alongside unarchived ones
- **THEN** no status bucket (including All) lists an archived item, and the All count equals the number of unarchived items

### Requirement: Total downloaded size shown in the status bar
The main-window status bar SHALL display the cumulative downloaded size across EVERY record, archived
included (human-readable), next to the total speed, updated live. This total is deliberately the one
place archived downloads still count: a status bucket's number must equal the rows selecting it shows,
whereas this total answers how much the app has fetched, and archiving keeps both the record and the
file. The total speed beside it SHALL exclude archived downloads, which are never running.

#### Scenario: Total downloaded reflects the sum
- **WHEN** several downloads have downloaded bytes
- **THEN** the status bar shows the sum of their downloaded bytes as a human-readable size beside the speed, and it updates as bytes arrive

#### Scenario: Archiving does not shrink the total
- **WHEN** a download with downloaded bytes is archived
- **THEN** the status bar total is unchanged, and it still includes that download's bytes

### Requirement: Column sorting is tri-state and drag-friendly
Clicking a sortable column header SHALL cycle its sort through Ascending, Descending, then None (no sort). In the None state the grid SHALL show items in their master (manual/priority) order and drag-to-reorder SHALL be enabled. When the user starts dragging a row while a sort is active, the sort SHALL be cleared to None (preserving the current visual order) so the drop reorders from there.

#### Scenario: Header click cycles three states
- **WHEN** the user clicks the same column header three times
- **THEN** the sort goes Ascending, then Descending, then None (master order restored)

#### Scenario: Dragging clears an active sort
- **WHEN** a column sort is active and the user begins dragging a row to reorder it
- **THEN** the sort is cleared to None, the visible order is preserved, and the drop reorders the item in master order (which persists)

### Requirement: Select-all checkbox aligns over the row checkboxes
The header select-all checkbox SHALL be horizontally aligned with the per-row selection checkboxes so
the selection column reads as a single aligned column. The checkbox SHALL act only on the rows
currently visible under the active filters: checking it SHALL select exactly the visible rows, and
its checked/indeterminate state SHALL reflect only those rows. When any filter is active, the
toolbar SHALL show how many downloads are selected, so the user can see the size of the action
before taking it.

#### Scenario: Header checkbox sits above the row checkboxes
- **WHEN** the downloads grid is shown with rows
- **THEN** the select-all checkbox is centered over the per-row checkbox column (no visible
  horizontal offset)

#### Scenario: Select-all covers only the visible rows
- **WHEN** 24 downloads exist, a category filter narrows the grid to 8 rows, and the user clicks
  select-all
- **THEN** exactly those 8 downloads are selected and the other 16 are not

#### Scenario: A bulk action reaches only the selected rows
- **WHEN** a category filter narrows the grid to 8 rows, the user selects all and removes them
- **THEN** only those 8 downloads are removed and the 16 filtered-out downloads remain

#### Scenario: The header state reflects the visible rows only
- **WHEN** every visible row is checked while filtered-out downloads are unchecked
- **THEN** the select-all checkbox reads as fully checked, not indeterminate

#### Scenario: The selected count is shown while filtering
- **WHEN** a filter is active and 8 downloads are selected
- **THEN** the toolbar states that 8 downloads are selected

### Requirement: A row's checkbox is its selection
Each downloads-grid row's checkbox SHALL always show whether that row is selected; there SHALL be no
separate "highlighted but unchecked" or "checked but not highlighted" state. A plain click on a row
SHALL select only that row. A Ctrl+click (Cmd+click on macOS) SHALL toggle that row and keep the rest
of the selection. A Shift+click SHALL select the range from the last clicked row. Clicking a row's
checkbox SHALL toggle only that row and keep the rest of the selection. The select-all checkbox SHALL
select or unselect all visible rows. A row hidden by a filter SHALL NOT remain selected. Every action
that works on "the selected downloads" SHALL act on exactly the checked rows.

#### Scenario: A click selects one row
- **WHEN** row A is checked and the user clicks row B
- **THEN** row B is checked and selected and row A is unchecked and unselected

#### Scenario: Ctrl+click adds a row
- **WHEN** row A is selected and the user Ctrl+clicks row B
- **THEN** rows A and B are both checked and selected

#### Scenario: Ctrl+click removes a row
- **WHEN** rows A and B are selected and the user Ctrl+clicks row B
- **THEN** only row A is checked and selected

#### Scenario: Shift+click selects a range
- **WHEN** the user clicks row 2 and then Shift+clicks row 5
- **THEN** rows 2 to 5 are checked and selected and no other row is

#### Scenario: Clicking a checkbox keeps the others
- **WHEN** row A is selected and the user clicks row B's checkbox
- **THEN** rows A and B are both checked and selected

#### Scenario: Actions follow the checkboxes
- **WHEN** exactly rows A and B are checked and the user clicks the toolbar Pause button
- **THEN** A and B are paused and no other download is

#### Scenario: A filtered-out row is unselected
- **WHEN** rows A and B are selected and a filter hides row B
- **THEN** only row A is selected, and clearing the filter shows row B unchecked

### Requirement: The Name cell shows the download's type icon
The Name cell SHALL show the download's category icon, in the category's color, immediately before
the file name on the same line. Hovering the icon SHALL name the category.

#### Scenario: Icon before the name
- **WHEN** the grid lists a `.mp4` download in the Video category
- **THEN** its Name cell shows the Video icon in the Video color, then the file name

#### Scenario: Hovering names the category
- **WHEN** the user hovers the icon in a Name cell
- **THEN** a tooltip names that download's category

### Requirement: Download rows carry no action buttons
Download rows SHALL NOT show per-row action buttons. Row actions SHALL be reached through the row's
right-click menu, the toolbar, and double-click (which opens the Details window).

#### Scenario: No action strip
- **WHEN** the downloads grid is shown
- **THEN** no row shows pause, resume, stop, open, folder, archive or remove buttons
