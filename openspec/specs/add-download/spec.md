# add-download Specification

## Purpose

Pre-download file name/size resolution for a single link and input handling for single vs multiple links in the Add dialog.
## Requirements
### Requirement: Single-link name and size pre-resolution
When exactly one link is entered in the Add dialog, the app SHALL resolve the remote file name and size before the download starts (debounced and non-blocking), prefill the File name box with the resolved name, and display the size in the dialog.

#### Scenario: Single link resolves a name and size
- **WHEN** the user enters a single valid link in the Add dialog
- **THEN** after a short debounce the app queries the remote file via the engine resolver
- **AND** prefills the File name box with the resolved name
- **AND** shows the resolved size in the dialog

#### Scenario: URL changes update auto-managed filename
- **WHEN** the File name box currently contains an auto-filled name
- **AND** the user changes the Add dialog input to a different single valid link
- **THEN** after debounce the app resolves the new link
- **AND** replaces the File name box value with the new resolved file name

#### Scenario: Manual filename override is preserved
- **WHEN** the user manually edits the File name box for a single-link download
- **AND** a later resolver result arrives for that same link or a new single link
- **THEN** the app keeps the user-entered file name
- **AND** does not overwrite it unless the user clears the File name box

#### Scenario: Resolution is non-blocking
- **WHEN** name/size resolution is in progress for a single link
- **THEN** the dialog remains usable and shows a transient "Resolving…" indication rather than blocking the UI

#### Scenario: Resolution fails or times out
- **WHEN** the single link cannot be resolved (network error, timeout, invalid URL)
- **THEN** the dialog does not block and the user can still proceed (name falls back to the URL-derived name)

### Requirement: Unknown size downloads as a single part
When the resolved remote file has no known size, the app SHALL download it as a single part rather than attempting multipart chunking.

#### Scenario: Server reports no content length
- **WHEN** a download's remote size is unknown after resolution
- **THEN** the download runs as one part (single connection)

### Requirement: Multiple-link input disables the File name box
When more than one link is entered in the Add dialog, the app SHALL disable the File name box because per-file names do not apply, and require only the destination folder.

#### Scenario: Multiple links entered
- **WHEN** the user enters more than one link in the Add dialog
- **THEN** the File name box is disabled
- **AND** only the destination folder is required to proceed

#### Scenario: Reverting to a single link re-enables the box
- **WHEN** the input is reduced back to a single link
- **THEN** the File name box is re-enabled and single-link resolution applies

### Requirement: Clipboard URL suggestion on empty Add dialog
When the Add dialog opens with no URL already entered and the system clipboard contains text that parses into one or more valid http/https URLs, the app SHALL display that content as a visually distinct, non-committed suggestion in the URL box, without altering the actual (bound) URL value until the user explicitly accepts it.

#### Scenario: Clipboard holds a single valid URL
- **WHEN** the Add dialog opens with an empty URL box
- **AND** the clipboard contains a single valid http/https URL
- **THEN** the URL box shows that URL as a dimmed/placeholder-style suggestion
- **AND** the dialog's `CanDownload`/parsed-URL state is unaffected until the suggestion is accepted

#### Scenario: Clipboard holds multiple valid URLs
- **WHEN** the Add dialog opens with an empty URL box
- **AND** the clipboard contains multiple valid http/https URLs separated by the same separators the dialog already accepts (newlines, spaces, tabs, commas, semicolons)
- **THEN** all of them are shown together as the suggestion, exactly as if the user had pasted them

#### Scenario: Accepting the suggestion
- **WHEN** a clipboard suggestion is showing and the user presses Enter or Tab while the URL box is still empty
- **THEN** the suggested text becomes the real URL box content
- **AND** normal parsing, validation, and single-link name/size resolution proceed as if the user had typed or pasted it

#### Scenario: No suggestion when clipboard has no valid URL
- **WHEN** the Add dialog opens with an empty URL box
- **AND** the clipboard is empty or does not parse into any valid http/https URL
- **THEN** no suggestion is shown and the dialog behaves exactly as it does today

#### Scenario: No suggestion when a URL is already present
- **WHEN** the Add dialog opens already seeded with a URL (e.g. from a browser-extension capture or a pasted link)
- **THEN** no clipboard suggestion is shown, and the existing seeded URL is left untouched

#### Scenario: Typing ignores the suggestion
- **WHEN** a clipboard suggestion is showing
- **AND** the user types or pastes different text instead of accepting it
- **THEN** the suggestion disappears and the dialog behaves exactly as it does today with the user's own input

### Requirement: Create a queue from the Add dialog
The Add Download dialog SHALL offer an "Add queue" action (styled like the Add button, placed to its
left) that creates a new named queue inline, refreshes the dialog's queue picker, and selects the new
queue so the download(s) being added are assigned to it.

#### Scenario: New queue created and used
- **WHEN** the user clicks "Add queue" in the Add dialog, enters a name and confirms
- **THEN** the queue is created through the app's queue system (visible on the Queues page)
- **AND** the dialog's queue picker appears/refreshes with the new queue selected
- **AND** starting the download assigns it to that queue

#### Scenario: Empty name is ignored
- **WHEN** the user confirms an empty/whitespace queue name
- **THEN** no queue is created and the dialog stays usable

