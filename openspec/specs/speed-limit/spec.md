# speed-limit Specification

## Purpose
How the global download speed limit and per-item speed overrides interact and persist.
## Requirements
### Requirement: Global speed limit propagates live to unrestricted downloads
When the user changes the global speed limit in Settings, the app SHALL apply the new value immediately to
every download that does not have its own custom speed limit — whether that download is currently running
or stopped — without requiring the download to be restarted.

#### Scenario: Global limit changes while a download is running
- **WHEN** a download without a custom limit is running and the user changes the global speed limit
- **THEN** the running download's effective speed limit changes immediately to the new global value

#### Scenario: Global limit changes while a download is stopped
- **WHEN** a download without a custom limit is stopped and the user changes the global speed limit
- **THEN** the download uses the new global value the next time it starts

#### Scenario: A custom-limited download is not touched
- **WHEN** a download has its own custom speed limit set and the user changes the global speed limit
- **THEN** that download's effective speed limit is unchanged

### Requirement: A per-item custom speed limit persists
The user SHALL be able to set a speed limit for a single download that overrides the global setting; once
set, it SHALL survive pausing, stopping, resuming, retrying, and an application restart, until the user
explicitly reverts that download to the global limit.

#### Scenario: Custom limit survives stop and resume
- **WHEN** the user sets a custom speed limit on a download, then stops and resumes it
- **THEN** the download resumes using its custom limit, not the current global value

#### Scenario: Custom limit survives an app restart
- **WHEN** the user sets a custom speed limit on a download and restarts the application
- **THEN** the download still uses its custom limit when it is next started

#### Scenario: Reverting to the global limit
- **WHEN** the user explicitly reverts a custom-limited download back to "use global limit"
- **THEN** it immediately uses the current global speed limit
- **AND** future global limit changes apply to it again
