# local-api Specification

## Purpose

Loopback HTTP JSON API on `127.0.0.1:15151` so scripts and the CLI can add, list and control downloads programmatically (issue #2), alongside the legacy browser-extension endpoints.
## Requirements
### Requirement: Silent programmatic add
The local API SHALL accept an add request on `/api/add` (POST JSON body or GET query parameters) with a required `url` and optional `filename`, `path`, `queue`, `mirrors` and `start` parameters, add the download silently (no dialog), and auto-start it unless `start` is false.

#### Scenario: Minimal add auto-starts
- **WHEN** a client sends `POST /api/add` with body `{"url":"https://example.com/file.zip"}` while the integration toggle is on
- **THEN** the app adds a download for that URL to the default queue using the configured save folder
- **AND** starts it (subject to the queue's concurrency cap) without showing any dialog
- **AND** responds `201` with a JSON object containing the new item's `id`

#### Scenario: Add with filename, path and no-start
- **WHEN** a client sends an add request with `filename`, `path` and `"start":false`
- **THEN** the item is created with that file name and save folder, in a stopped/queued state, and is not started

#### Scenario: Invalid URL is rejected
- **WHEN** a client sends an add request whose `url` is missing or not http/https
- **THEN** the API responds `400` with a JSON `error` message and no item is added

### Requirement: Download list endpoint
The local API SHALL expose `GET /api/list` returning a JSON array of every download with its `id`, `name`, `url`, `status`, `progress`, `size`, `downloaded`, `speed`, `folder`, `filePath` and `queue`.

#### Scenario: List reflects live state
- **WHEN** a client requests `GET /api/list` while one download is running
- **THEN** the response contains one entry per download in the app
- **AND** the running item's entry reports its current status, progress and speed

### Requirement: Per-item control endpoints
The local API SHALL expose `pause`, `resume`, `cancel`, `retry` and `remove` endpoints that act on a single download identified by its `id`, returning `200` on success and `404` for an unknown id. Actions inapplicable to the item's current state SHALL be safe no-ops (existing manager state guards) and still return `200`.

#### Scenario: Pause a running download
- **WHEN** a client sends `POST /api/pause` with the id of a running download
- **THEN** the download is paused and the API responds `200`

#### Scenario: Unknown id
- **WHEN** a client sends a control request with an id that matches no download
- **THEN** the API responds `404` with a JSON `error` message

#### Scenario: Inapplicable action is idempotent
- **WHEN** a client sends `POST /api/pause` for a download that is already completed
- **THEN** the download's state is unchanged and the API responds `200`

### Requirement: Loopback-only, enabled by default, extension-compatible listener
The local API SHALL bind only to `127.0.0.1`, run while the integration toggle is enabled, and keep the legacy `GET /add?url=` (opens the Add dialog pre-filled) and `GET /ping` endpoints behaving exactly as before. The toggle SHALL default to enabled for new installs, and existing configurations that predate this change SHALL be migrated to enabled once (a value the user later sets is respected). New `/api/*` responses SHALL NOT include CORS allow headers.

#### Scenario: Enabled by default on a new install
- **WHEN** the app runs with a fresh configuration
- **THEN** the integration toggle is on and the local API is listening on the loopback port

#### Scenario: Existing config migrated once
- **WHEN** the app loads a configuration saved before this change
- **THEN** the integration toggle is turned on one time as part of loading

#### Scenario: Toggle off means no API
- **WHEN** the user turns the integration toggle off
- **THEN** nothing listens on the API port and API requests fail to connect

#### Scenario: Browser extension endpoints unchanged
- **WHEN** the extension sends `GET /add?url=…` or `GET /ping`
- **THEN** the responses and behavior (Add dialog pre-fill, 200 health check, CORS header) are identical to the pre-change behavior

#### Scenario: No CORS on API routes
- **WHEN** a client requests any `/api/*` endpoint
- **THEN** the response contains no `Access-Control-Allow-Origin` header

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

