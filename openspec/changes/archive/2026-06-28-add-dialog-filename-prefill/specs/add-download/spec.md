## MODIFIED Requirements

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
