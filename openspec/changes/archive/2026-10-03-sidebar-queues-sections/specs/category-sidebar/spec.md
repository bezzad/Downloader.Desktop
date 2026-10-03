## MODIFIED Requirements

### Requirement: The category sidebar is optional and off by default
The main window SHALL offer a left sidebar listing the categories and the queues. It SHALL be shown on
first run and SHALL be shown or hidden by a single toggle button placed immediately to the left of the
"Paste download link" box. The sidebar SHALL have exactly two states — shown or hidden — with no
intermediate icons-only state. A configuration saved by an earlier version SHALL have the sidebar
shown once after upgrading; from then on the user's choice SHALL be kept.

#### Scenario: Shown on first run
- **WHEN** the app is started for the first time
- **THEN** the sidebar is shown beside the downloads grid

#### Scenario: Shown once after upgrading
- **WHEN** a user whose saved configuration has the sidebar hidden upgrades to this version
- **THEN** the sidebar is shown on the first start; if they hide it again, it stays hidden on later
  starts

#### Scenario: The toggle shows and hides it
- **WHEN** the user clicks the toggle button
- **THEN** the sidebar hides; clicking it again shows the sidebar

#### Scenario: The toggle is reachable whether or not the sidebar is open
- **WHEN** the sidebar is open
- **THEN** the toggle button is still visible in its place beside the link box

### Requirement: The sidebar lists every category with its icon, name and count
The sidebar SHALL have two collapsible sections, each with a header that expands or collapses it and
both expanded at start: a Categories section listing an "All" entry followed by every category in the
user's order, and a Queues section listing every queue in the configured order. Each category row
SHALL show its icon, its name and the number of downloads currently in it; each queue row SHALL show
its name and the number of its downloads. Counts SHALL respect the active status filter and search.
A category or queue with no downloads SHALL remain listed, visually de-emphasized rather than hidden.
Queue rows SHALL follow the live queue set: a new, renamed or removed queue SHALL be shown at once.

#### Scenario: Counts are shown per category
- **WHEN** the user has five video downloads and two audio downloads
- **THEN** the Video row shows 5 and the Audio row shows 2

#### Scenario: Queues are listed with counts
- **WHEN** the queue "Main" holds two downloads and the queue "Night" holds three
- **THEN** the Queues section lists Main with 2 and Night with 3

#### Scenario: A section can be collapsed
- **WHEN** the user clicks the Queues section header
- **THEN** the queue rows are hidden; clicking it again shows them

#### Scenario: An empty category stays listed
- **WHEN** a category contains no downloads
- **THEN** it is still listed, shown de-emphasized, and does not disappear or reappear as downloads
  arrive

#### Scenario: Counts respect the active status filter
- **WHEN** the status filter is set to Failed and three of the eight video downloads have failed
- **THEN** the Video row shows 3

#### Scenario: A new queue appears at once
- **WHEN** the user creates a queue from the Queues page or the Add dialog
- **THEN** it appears in the sidebar's Queues section without a restart

### Requirement: Selecting a category filters the whole downloads list
Clicking a category SHALL filter the downloads list to that category, and clicking a queue SHALL filter
it to that queue's downloads. Only one sidebar row SHALL be selected at a time: selecting a queue SHALL
clear the category filter, selecting a category SHALL clear the queue filter, and selecting "All" SHALL
clear both. The filter SHALL apply to every download regardless of its state, and SHALL combine with
the status filter and the search term. If the selected queue is removed, the selection SHALL fall back
to "All".

#### Scenario: An in-progress download is filtered like any other
- **WHEN** the user selects the Video category while a video is downloading and an archive is
  downloading
- **THEN** only the video download is listed

#### Scenario: Selecting a queue shows its downloads
- **WHEN** the user selects the queue "Night"
- **THEN** only downloads in Night are listed, whatever their state

#### Scenario: One selection across both sections
- **WHEN** the Video category is selected and the user selects the queue "Night"
- **THEN** the category filter is cleared, only Night's downloads are listed, and only the Night row
  is marked selected

#### Scenario: The sidebar filter combines with the other filters
- **WHEN** the user selects the Video category with the status filter on Completed and a search term
  entered
- **THEN** the list shows only completed videos matching the search term

#### Scenario: "All" clears only the sidebar dimension
- **WHEN** a queue is selected, a status filter and a search term are active, and the user clicks "All"
- **THEN** the queue filter is cleared and the status filter and search term remain in effect

#### Scenario: Removing the selected queue
- **WHEN** the selected queue is removed
- **THEN** "All" becomes the selected row and the list is no longer filtered by queue

### Requirement: Categories are managed from the sidebar
The sidebar SHALL offer a low-emphasis "Add" entry below the category list that opens the
category editor for a new category, and SHALL let the user open the editor for an existing category
and change a category's position in the list — from its right-click menu (Move up / Move down) or by
dragging it. Hovering a category row other than "All" SHALL show a six-dot drag handle; dragging it
up or down SHALL move the category to the drop position, which SHALL be persisted. Nothing SHALL be
dropped above "All", and queue rows SHALL NOT be draggable.

#### Scenario: Adding a category from the sidebar
- **WHEN** the user clicks the "Add" entry, enters a name, picks an icon and a color, and confirms
- **THEN** the new category appears at the end of the sidebar list and is persisted

#### Scenario: Editing a category from the sidebar
- **WHEN** the user opens an existing category's editor and changes its name
- **THEN** the sidebar shows the new name immediately

#### Scenario: A category's position can be changed from the sidebar
- **WHEN** the user moves a category up in the sidebar
- **THEN** it is listed above its former neighbour and the new order is persisted

#### Scenario: The drag handle appears on hover
- **WHEN** the pointer is over the Video category row
- **THEN** a six-dot handle is shown on that row; it is not shown on "All" or on queue rows

#### Scenario: Dragging reorders categories
- **WHEN** the user drags the Documents category above the Video category and releases
- **THEN** Documents is listed above Video, and the order is the same after a restart

## ADDED Requirements

### Requirement: The selected sidebar row carries an accent bar
The selected sidebar row — category, queue or "All" — SHALL show a vertical bar on its starting edge
in the current theme accent color, in addition to its selected background. The bar SHALL change color
at once when the user picks another accent, and SHALL sit on the right edge in right-to-left languages.

#### Scenario: Accent bar on the selected row
- **WHEN** the Video category is selected
- **THEN** a vertical bar in the accent color is shown on the left edge of the Video row, and on no
  other row

#### Scenario: The bar follows the accent
- **WHEN** the user changes the accent color in Settings
- **THEN** the selected row's bar uses the new accent color without a restart

#### Scenario: Right-to-left layout
- **WHEN** the app language is Persian
- **THEN** the bar is on the right edge of the selected row
