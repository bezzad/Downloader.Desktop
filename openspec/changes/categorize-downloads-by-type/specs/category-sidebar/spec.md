## ADDED Requirements

### Requirement: The category sidebar is optional and off by default
The main window SHALL offer a left sidebar listing the categories. It SHALL be hidden on first run
and SHALL be shown or hidden by a single toggle button placed immediately to the left of the
"Paste download link" box. The sidebar SHALL have exactly two states — shown or hidden — with no
intermediate icons-only state.

#### Scenario: Hidden on first run
- **WHEN** the app is started for the first time
- **THEN** the category sidebar is not shown and the downloads grid uses the full window width

#### Scenario: The toggle shows and hides it
- **WHEN** the user clicks the toggle button
- **THEN** the sidebar appears; clicking it again hides the sidebar

#### Scenario: The toggle is reachable whether or not the sidebar is open
- **WHEN** the sidebar is open
- **THEN** the toggle button is still visible in its place beside the link box

### Requirement: The sidebar's state survives a restart
Whether the sidebar is shown SHALL be persisted and restored on the next launch.

#### Scenario: Open stays open
- **WHEN** the user opens the sidebar, quits the app and starts it again
- **THEN** the sidebar is open

#### Scenario: Closed stays closed
- **WHEN** the user closes the sidebar, quits the app and starts it again
- **THEN** the sidebar is closed

### Requirement: The sidebar lists every category with its icon, name and count
The sidebar SHALL list an "All" entry followed by every category in the user's order, each showing
its own icon, its name and the number of downloads currently in it. A category with no downloads
SHALL remain listed, visually de-emphasized rather than hidden.

#### Scenario: Counts are shown per category
- **WHEN** the user has five video downloads and two audio downloads
- **THEN** the Video row shows 5 and the Audio row shows 2

#### Scenario: An empty category stays listed
- **WHEN** a category contains no downloads
- **THEN** it is still listed, shown de-emphasized, and does not disappear or reappear as downloads
  arrive

#### Scenario: Counts respect the active status filter
- **WHEN** the status filter is set to Failed and three of the eight video downloads have failed
- **THEN** the Video row shows 3

### Requirement: Selecting a category filters the whole downloads list
Clicking a category SHALL filter the downloads list to that category. The filter SHALL apply to
every download regardless of its state — running, queued, paused, failed or completed — not only to
completed ones. Selecting "All" SHALL clear the category filter only.

#### Scenario: An in-progress download is filtered like any other
- **WHEN** the user selects the Video category while a video is downloading and an archive is
  downloading
- **THEN** only the video download is listed

#### Scenario: The category filter combines with the other filters
- **WHEN** the user selects the Video category with the status filter on Completed and a search term
  entered
- **THEN** the list shows only completed videos matching the search term

#### Scenario: "All" clears only the category dimension
- **WHEN** a status filter and a search term are active and the user clicks "All"
- **THEN** the category filter is cleared and the status filter and search term remain in effect

#### Scenario: The selected category is indicated
- **WHEN** a category is selected
- **THEN** it is visually marked as the active one in the sidebar

### Requirement: An empty result explains itself
When the active filters match no downloads, the list SHALL say so and SHALL offer a way to clear the
filters.

#### Scenario: No matches under a category filter
- **WHEN** the user selects a category containing no downloads
- **THEN** the list shows an empty-state message and an action that clears the filters

### Requirement: Categories are managed from the sidebar
The sidebar SHALL offer a low-emphasis "Add" entry below the category list that opens the
category editor for a new category, and SHALL let the user open the editor for an existing category
and change a category's position in the list.

#### Scenario: Adding a category from the sidebar
- **WHEN** the user clicks the "Add" entry, enters a name, picks an icon and a color, and confirms
- **THEN** the new category appears at the end of the sidebar list and is persisted

#### Scenario: Editing a category from the sidebar
- **WHEN** the user opens an existing category's editor and changes its name
- **THEN** the sidebar shows the new name immediately

#### Scenario: A category's position can be changed from the sidebar
- **WHEN** the user moves a category up in the sidebar
- **THEN** it is listed above its former neighbour and the new order is persisted

### Requirement: Category icons are chosen from the app's own icon set
A category's icon SHALL be chosen from the icons the app ships with, identified by a stable key,
and SHALL be paired with a user-chosen color. A category SHALL NOT reference an image file on disk.

#### Scenario: Picking an icon and a color
- **WHEN** the user picks an icon and a color for a category
- **THEN** that icon in that color is shown for the category in the sidebar and in the grid's Type
  column

#### Scenario: An unrecognized icon key degrades safely
- **WHEN** a category carries an icon key this version of the app does not recognize
- **THEN** a default icon is shown, no error is raised, and the original key is preserved in the
  configuration so a later version can render it correctly
