# local-api Specification (delta)

## MODIFIED Requirements

### Requirement: Silent programmatic add
The local API SHALL accept an add request on `/api/add` (POST JSON body or GET query parameters) with a required `url` and optional `filename`, `path`, `queue`, `mirrors` and `start` parameters, add the download silently (no dialog), and auto-start it unless `start` is false.

The POST JSON body SHALL additionally accept an optional `headers` object (string→string) and an optional `referer` string, which apply to **that download only** and override the app's global request settings.

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

#### Scenario: Add with per-download headers and referer
- **WHEN** a client sends `POST /api/add` with a `headers` object and a `referer` string alongside the URL
- **THEN** the item is created carrying those headers and that referer
- **AND** every request the app makes for that download sends them

#### Scenario: Malformed request context does not fail the add
- **WHEN** an add request's `headers` is not an object, or contains entries whose name or value is not a string
- **THEN** those entries are ignored and the download is still added

## ADDED Requirements

### Requirement: Extension-supplied cookies are used for the whole download
When an add request carries a `cookies` array, the app SHALL use those cookies both to resolve the link and to fetch its bytes, for the life of the item in this session.

#### Scenario: Cookies reach the byte-fetching requests
- **WHEN** a download is added with cookies for a session-gated URL
- **THEN** the requests that download the file (not only the resolve step) send those cookies

#### Scenario: Retry re-sends the cookies
- **WHEN** a download added with cookies fails and the user retries it in the same session
- **THEN** the retry sends the same cookies rather than requesting anonymously
