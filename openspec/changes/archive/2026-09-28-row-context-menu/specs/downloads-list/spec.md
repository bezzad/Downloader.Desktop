## ADDED Requirements

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

## REMOVED Requirements

### Requirement: A Type column shows each download's category
**Reason**: The author asked for the type icon to sit beside the file name instead of in its own
column; the column's width is given back to the grid.
**Migration**: The icon and its category tooltip are now in the Name cell. Filtering by category is
done from the category sidebar. Sorting by type is no longer available.
