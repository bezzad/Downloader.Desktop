## ADDED Requirements

### Requirement: Listen port falls back within a declared range
The local API SHALL attempt to bind to a small, pre-declared range of loopback ports starting from the preferred default (`15151`), trying each subsequent port in the range in order until one succeeds, so that another process already holding the preferred port does not disable the local API entirely.

#### Scenario: Preferred port is free
- **WHEN** the app starts and port `15151` is free
- **THEN** the local API binds to `15151` as before

#### Scenario: Preferred port is taken, a fallback is free
- **WHEN** the app starts and port `15151` is already bound by another process
- **AND** a later port within the declared range (`15152`–`15155`) is free
- **THEN** the local API binds to that free port instead
- **AND** the effective port is persisted so a later restart can prefer it

#### Scenario: Entire range is taken
- **WHEN** every port in the declared range is already bound by other processes
- **THEN** the local API fails to start exactly as it does today (soft failure, app continues running)
- **AND** the effective/reachable status reflects that the local API is not running

### Requirement: Effective port is surfaced to the user
Settings SHALL show the local API's current listen address and a live reachable/not-reachable status.

#### Scenario: Local API is running
- **WHEN** the local API is bound and listening
- **THEN** Settings shows its address (e.g. `127.0.0.1:15151`) and a "connected"/reachable indicator

#### Scenario: Fallback port notification
- **WHEN** the local API falls back to a port other than the preferred `15151`
- **THEN** the user is shown a one-time notification stating which port is now in use
