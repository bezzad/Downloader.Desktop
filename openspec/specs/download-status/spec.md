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

### Requirement: Stopped state survives an app restart
A download the user explicitly stopped (individually or via Stop All) SHALL remain Stopped after the
application is closed and reopened, and SHALL NOT be automatically re-queued or started by any background
mechanism (including the scheduler re-evaluating a schedule whose window already contains the current time
of day, if that schedule already fired earlier the same day).

#### Scenario: Stop All survives a restart with no schedules configured
- **WHEN** the user stops all downloads and restarts the application, with no schedules configured
- **THEN** every previously-stopped download is still Stopped after the restart
- **AND** none of them starts downloading on their own

#### Scenario: An already-fired-today schedule does not re-fire on restart
- **WHEN** an enabled schedule's start window already fired earlier today, the user then stops all
  downloads, and restarts the application while still inside that schedule's window
- **THEN** the schedule does not re-trigger a start on the restart
- **AND** the stopped downloads remain Stopped

#### Scenario: A schedule that has not yet fired today still fires normally
- **WHEN** an enabled schedule's window contains the current time of day and it has not fired yet today
- **THEN** it fires normally (on the current tick or after a restart), starting its target queue once
