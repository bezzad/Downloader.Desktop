## ADDED Requirements

### Requirement: A download can be archived and restored
The app SHALL let the user archive one or more downloads and later restore them. An archived
download keeps its record, its file on disk and its history; it is removed from the working list but
is not deleted. The archived flag SHALL survive an app restart.

#### Scenario: Archiving takes the row out of the working list
- **WHEN** the user archives a download
- **THEN** the download no longer appears under any status filter, including All, and its record is kept

#### Scenario: Restoring puts the row back
- **WHEN** the user restores an archived download
- **THEN** it appears again under the status filter matching its state, with its progress, size and file unchanged

#### Scenario: The archived flag survives a restart
- **WHEN** the user archives a download and restarts the app
- **THEN** the download is still archived after the restart

### Requirement: An archived download is never active
Archiving SHALL stop a download that is running, paused or queued before marking it archived, and
starting, resuming or retrying an archived download SHALL un-archive it first. No code path may
leave a download both archived and running or queued.

#### Scenario: Archiving a running download stops it
- **WHEN** the user archives a download that is currently running
- **THEN** the download is stopped first and then archived, and it is not restarted by the queue

#### Scenario: Starting an archived download restores it
- **WHEN** the user starts, resumes or retries an archived download
- **THEN** the download is no longer archived and appears in the working list in its new state

#### Scenario: Bulk start ignores archived downloads
- **WHEN** an archived unfinished download exists and the user triggers "start all" or starts a queue
- **THEN** the archived download stays archived and is not started

### Requirement: Archived downloads take no part in queues or totals
Archived downloads SHALL be excluded from the queue concurrency pump, from bulk start/stop actions,
from the Queues page listings and counts, and from the status bar speed and downloaded totals.

#### Scenario: The queue pump skips archived items
- **WHEN** a queue has a free concurrency slot and its next item is archived
- **THEN** the pump skips it and considers the following item

#### Scenario: Queues page hides archived items
- **WHEN** a queue contains an archived download
- **THEN** the queue card does not list it and does not count it in its running, waiting, done or failed figures

### Requirement: The archived view is reached from a footer filter
The footer filter row SHALL include an Archived pill showing the number of archived downloads.
Selecting it SHALL list exactly the archived downloads, whatever their state, and no others. The
search box SHALL apply within the archived view.

#### Scenario: The archived pill lists the archived downloads
- **WHEN** the user selects the Archived filter
- **THEN** the list shows every archived download and nothing else, and the pill's count equals that number

#### Scenario: Search applies inside the archived view
- **WHEN** the Archived filter is selected and the user types into the search box
- **THEN** the archived list is narrowed to the archived downloads matching the search

### Requirement: Archive and Restore are reachable per row and in bulk
Each download row SHALL offer an Archive action in its action strip, shown as Restore while the
archived view is active. The toolbar SHALL offer an Archive button next to Remove, enabled by the
current selection in the same way as Start, Pause and Stop. While the Archived filter is active the
toolbar's download actions SHALL be Restore and Remove only.

#### Scenario: Row action archives a single download
- **WHEN** the user clicks the Archive icon on a download row
- **THEN** that download is archived and leaves the list

#### Scenario: Toolbar archives the selected downloads
- **WHEN** several downloads are selected and the user clicks the toolbar Archive button
- **THEN** every selected download is archived

#### Scenario: Toolbar switches to Restore in the archived view
- **WHEN** the Archived filter is selected
- **THEN** the toolbar shows Restore and Remove instead of Start, Pause, Stop and Archive

#### Scenario: Archive is disabled with no selection
- **WHEN** no download is selected
- **THEN** the toolbar Archive button is visible but disabled, exactly as Start, Pause and Stop are
