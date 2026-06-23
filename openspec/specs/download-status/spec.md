# download-status Specification

## Purpose

Status-filter buckets in the main window and detection of expired/invalid download links.

## Requirements

### Requirement: Failed filter shows only real failures
The Failed status filter in the main window SHALL show only downloads that genuinely failed, and SHALL NOT include user-stopped downloads.

#### Scenario: Stopped item is not under Failed
- **WHEN** a download is stopped by the user and the Failed filter is selected
- **THEN** the stopped download is not shown in the list

#### Scenario: Failed item is under Failed
- **WHEN** a download has failed and the Failed filter is selected
- **THEN** the failed download is shown in the list

### Requirement: Stopped items appear under All
User-stopped downloads SHALL be visible under the All filter, which has no dedicated Stopped pill.

#### Scenario: Stopped item under All
- **WHEN** a download is stopped by the user and the All filter is selected
- **THEN** the stopped download is shown in the list

### Requirement: Expired or invalid link is marked Failed
A download whose response is non-file content (e.g. an HTML page) or an implausibly small text body SHALL be marked Failed with a clear "Link expired or invalid" message instead of a confusing partial or completed state.

#### Scenario: Server returns an HTML page instead of the file
- **WHEN** a download's response content type indicates HTML/text rather than the expected file
- **THEN** the download is marked Failed with a "Link expired or invalid" message

#### Scenario: Response is implausibly small text
- **WHEN** a download finishes with a tiny text body that cannot be the requested file
- **THEN** the download is marked Failed with a "Link expired or invalid" message

#### Scenario: A genuine small file is not mis-flagged
- **WHEN** a download completes with real file content of the expected type
- **THEN** it is marked Completed and not flagged as expired/invalid
