## ADDED Requirements

### Requirement: Settings endpoint
The local API SHALL answer `GET /api/settings` with a JSON object describing the settings a local
client needs in order to pre-fill its own UI — at minimum the app's configured default save folder
and the app's version. The endpoint SHALL be read-only and SHALL NOT expose secrets (no cookies, no
headers, no credentials, no proxy password).

#### Scenario: A client reads the default save folder
- **WHEN** a client sends `GET /api/settings` while the integration toggle is on
- **THEN** the API responds `200` with a JSON object whose `defaultSavePath` is the folder the app is
  configured to save downloads in
- **AND** whose `version` is the running app's version

#### Scenario: The endpoint changes nothing
- **WHEN** a client sends `GET /api/settings`
- **THEN** no download is added, started or modified, and the app's settings are unchanged
