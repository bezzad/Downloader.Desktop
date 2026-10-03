## ADDED Requirements

### Requirement: Every row action is in the row's right-click menu
Right-clicking anywhere on a download row SHALL open a menu that acts on the selected downloads. If
the right-clicked row is not selected, it SHALL first become the only selected row. The menu SHALL
NOT have a title line. It SHALL list, in this order and grouped by separators: Open, Open folder;
Resume, Pause, Stop, Restart download; Move to queue (submenu), Category (submenu); Copy (submenu:
Copy link, Copy as JSON, Copy as cURL); the plugin-offered action when one exists; Archive (Restore in
the archived view), Delete; Properties.

#### Scenario: Right-click on an unselected row
- **WHEN** rows A and B are selected and the user right-clicks row C
- **THEN** only row C is selected and the menu acts on row C

#### Scenario: Right-click on a selected row
- **WHEN** rows A and B are selected and the user right-clicks row B and chooses Pause
- **THEN** both A and B are paused

#### Scenario: The menu has the full set of items
- **WHEN** the user right-clicks a row and there is more than one queue
- **THEN** the menu shows Open, Open folder, Resume, Pause, Stop, Restart download, Move to queue,
  Category, Copy, Archive, Delete and Properties, with no title line

### Requirement: Menu items are enabled only when they apply
A menu item SHALL be enabled when it applies to at least one selected download and disabled
otherwise, and SHALL act only on the selected downloads it applies to. Open SHALL apply to a completed
download; Resume to a paused, stopped, failed or queued download (a failed one is retried); Pause to a
running one; Stop to a running, paused or queued one; Restart download to one that is neither running
nor completed; Properties SHALL be enabled only when exactly one download is selected. Move to queue
SHALL be hidden when only one queue exists, and the plugin action SHALL be hidden when no plugin offers
one.

#### Scenario: Pause disabled for a finished download
- **WHEN** the only selected download is completed
- **THEN** Pause, Stop and Restart download are disabled and Open is enabled

#### Scenario: Resume retries a failed download
- **WHEN** a failed download is selected and the user chooses Resume
- **THEN** the download is retried

#### Scenario: Properties needs a single row
- **WHEN** two downloads are selected
- **THEN** Properties is disabled; with one selected it opens that download's Details window

#### Scenario: Move to queue is hidden with a single queue
- **WHEN** only the default queue exists
- **THEN** the menu does not show Move to queue

### Requirement: Restart download starts again from zero
Restart download SHALL stop the download if it is active, delete its partial file, reset its progress
to zero and queue it again under the queue's concurrency limit. It SHALL NOT be available for a
completed download and SHALL NOT delete any completed file.

#### Scenario: A stopped download restarts from zero
- **WHEN** a download stopped at 40 % is restarted
- **THEN** its partial file is deleted, its progress reads 0 % and it is queued to start again

#### Scenario: The queue limit is respected
- **WHEN** the queue's limit is already reached and the user restarts a stopped download
- **THEN** the download waits in the queue instead of starting at once

### Requirement: Downloads can be copied as a link, JSON or cURL
The Copy submenu SHALL copy the selected downloads to the clipboard: Copy link as one URL per line;
Copy as JSON as an array with each download's URL, mirrors, file name, folder and size; Copy as cURL
as one `curl` command per download that saves to the download's file name and includes the referer
when one is set. None of the formats SHALL include cookies or request headers.

#### Scenario: Copy as cURL
- **WHEN** the user copies one download with a referer as cURL
- **THEN** the clipboard holds a single `curl` command with the URL, the output file name and the
  referer, correctly quoted

#### Scenario: Secrets are never copied
- **WHEN** a download that was added with cookies and headers is copied as JSON or cURL
- **THEN** the copied text contains neither the cookies nor the headers

#### Scenario: Several rows are copied together
- **WHEN** three downloads are selected and the user chooses Copy link
- **THEN** the clipboard holds the three URLs, one per line

### Requirement: Menu shortcuts work on the grid
The menu SHALL show a keyboard shortcut next to Open (Ctrl+O), Resume (Ctrl+R), Pause (Ctrl+P), Copy
link (Ctrl+C), Delete (Delete) and Properties (Ctrl+I), using Cmd instead of Ctrl on macOS, and each
shortcut SHALL perform that item on the selected downloads while the downloads grid has keyboard focus.

#### Scenario: Delete key removes the selection
- **WHEN** the grid has focus with two downloads selected and the user presses Delete
- **THEN** the same removal as the menu's Delete runs for those two downloads

#### Scenario: Ctrl+C in a text box is not taken
- **WHEN** the search box has focus and the user presses Ctrl+C
- **THEN** the search text is copied, not a download link
