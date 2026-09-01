## ADDED Requirements

### Requirement: The extension identifies itself on the requests it already makes
The browser extension SHALL include its own version and a coarse browser label on the requests it already
sends to the local API, without requiring an additional browser permission and without issuing an extra
request. The app SHALL record only the most recently reported version, browser label and time, in memory
only. That information SHALL NOT be written to the configuration file and SHALL NOT be written to the log.

#### Scenario: Version and browser reach the app
- **WHEN** the extension sends a request to the local API
- **THEN** the app records the extension version and browser label it reported

#### Scenario: No extra request or permission
- **WHEN** the extension reports its identity
- **THEN** it does so on an existing request and requires no browser permission beyond those already granted

#### Scenario: Identity is not persisted or logged
- **WHEN** the app records a reported extension identity
- **THEN** it is not written to the configuration file and does not appear in the log

#### Scenario: A request without identity still works
- **WHEN** a request arrives with no extension identity, as an older extension or another tool would send
- **THEN** the request is handled exactly as before and no error is raised
