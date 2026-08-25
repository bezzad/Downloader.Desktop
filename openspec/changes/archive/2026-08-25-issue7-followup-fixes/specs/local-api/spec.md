## MODIFIED Requirements

### Requirement: Silent programmatic add
The local API SHALL accept an add request on `/api/add` (POST JSON body or GET query parameters) with a required `url` and optional `filename`, `path`, `queue`, `mirrors` and `start` parameters, add the download silently (no dialog), and auto-start it unless `start` is false.

Both request forms SHALL additionally accept an optional per-download request context — `cookies`, a `headers` mapping, and a `referer` — which applies to **that download only** and overrides the app's global request settings. In the POST body `headers` is a string→string object and `cookies` is an array of cookie objects. In the GET query `headers` and `cookies` are the wire forms a browser or capture tool already has to hand: `cookies` is a `name=value; name=value` Cookie-header string, and `headers` is a newline-separated `Name: value` block; both are URL-encoded like any other parameter.

The response to a successful add SHALL report how much request context was accepted, so a caller can distinguish a working hand-off from one whose context was dropped.

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

#### Scenario: Add with a request context through the GET query form
- **WHEN** a client sends `GET /api/add?url=…&referer=…&cookies=SID%3Dv%3B%20other%3Dw` while the integration toggle is on
- **THEN** the item is created carrying both cookies and that referer
- **AND** every request the app makes for that download sends them

#### Scenario: The caller can see that its context was accepted
- **WHEN** an add request carries cookies and headers and the item is created
- **THEN** the `201` response body reports the number of cookies and headers that were accepted

#### Scenario: Malformed request context does not fail the add
- **WHEN** an add request's `headers` is not an object (POST) or is not parseable (GET), or contains entries whose name or value is empty
- **THEN** those entries are ignored and the download is still added

#### Scenario: A cookie string with no usable pairs is not an error
- **WHEN** a `GET /api/add` request's `cookies` parameter is empty or contains no `name=value` pair
- **THEN** the download is added with no cookies and the response reports zero accepted cookies
