## ADDED Requirements

### Requirement: A Type column shows each download's category
The downloads grid SHALL carry a Type column, placed immediately before the Name column, showing the
download's category icon in the category's color. Hovering the icon SHALL name the category. The
column SHALL be sortable, ordering rows by the user's category order rather than alphabetically.

#### Scenario: The column shows the category icon
- **WHEN** the downloads grid lists a `.mp4` download in the Video category
- **THEN** the Type cell for that row shows the Video category's icon in its color

#### Scenario: Hovering names the category
- **WHEN** the user hovers the pointer over a Type cell
- **THEN** a tooltip names that download's category

#### Scenario: Sorting by type follows the user's category order
- **WHEN** the user sorts ascending on the Type column
- **THEN** rows are ordered by their category's position in the user's category list

#### Scenario: The Type column sits before Name
- **WHEN** the downloads grid is shown
- **THEN** the Type column appears immediately before the Name column

#### Scenario: The Name cell no longer repeats the type icon
- **WHEN** the downloads grid is shown
- **THEN** the Name cell shows the file name without a type icon, because the icon now lives in the
  adjacent Type column

## MODIFIED Requirements

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
