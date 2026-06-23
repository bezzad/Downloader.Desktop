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
