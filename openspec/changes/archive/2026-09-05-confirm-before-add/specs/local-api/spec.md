## MODIFIED Requirements

### Requirement: Silent programmatic add
The local API SHALL accept an add request on `/api/add` (POST JSON body or GET query parameters) with a required `url` and optional `filename`, `path`, `queue`, `mirrors`, `confirm` and `start` parameters, and — unless the request is in confirm mode — add the download silently (no dialog) and auto-start it unless `start` is false.

A request is in **confirm mode** when its `confirm` parameter is true, or when the app's "ask before adding programmatic downloads" setting is on and the request does not explicitly set `confirm` to false. An explicit `confirm` value in the request SHALL win over the setting in both directions.

Both request forms SHALL additionally accept an optional per-download request context — `cookies`, a `headers` mapping, and a `referer` — which applies to **that download only** and overrides the app's global request settings. In the POST body `headers` is a string→string object and `cookies` is an array of cookie objects. In the GET query `headers` and `cookies` are the wire forms a browser or capture tool already has to hand: `cookies` is a `name=value; name=value` Cookie-header string, and `headers` is a newline-separated `Name: value` block; both are URL-encoded like any other parameter.

The response to a successful add SHALL report how much request context was accepted, so a caller can distinguish a working hand-off from one whose context was dropped.

#### Scenario: Minimal add auto-starts
- **WHEN** a client sends `POST /api/add` with body `{"url":"https://example.com/file.zip"}` while the integration toggle is on and confirm mode is off
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

#### Scenario: An explicit confirm:false opts out of the app-wide setting
- **WHEN** the "ask before adding programmatic downloads" setting is on and a client sends an add request with `"confirm":false`
- **THEN** the download is added silently exactly as it would be with the setting off

## ADDED Requirements

### Requirement: A programmatic add can ask the user to confirm it
An add request in confirm mode SHALL NOT add the download. Instead the app SHALL surface its main window and open the Add dialog pre-filled with **everything the request carried** — url, filename, save path, queue, mirrors, variant, cookies, referer and headers — so the user reviews and may change the download before it exists.

The request SHALL NOT block on the user. The app SHALL answer `202` immediately with a JSON body containing a `ticket` identifying the pending confirmation, and SHALL resolve that ticket when the user confirms or cancels the dialog. A caller that ignores the `202` body SHALL still cause the dialog to appear, so a third-party client that knows nothing about tickets is served by the app-wide setting alone.

A confirmed dialog SHALL add the download exactly as a silent add would, including the per-download request context. A cancelled dialog SHALL add nothing.

#### Scenario: A confirm-mode add opens the dialog instead of adding
- **WHEN** a client sends `POST /api/add` with `"confirm":true` and a full request context
- **THEN** no download is added at that moment
- **AND** the app's window is surfaced and the Add dialog opens pre-filled with the URL, file name, save path and any mirrors and variant the request carried
- **AND** the API responds `202` with a `ticket`

#### Scenario: The app-wide setting turns every programmatic add into a confirmation
- **WHEN** the "ask before adding programmatic downloads" setting is on and a client sends an ordinary add request carrying no `confirm` parameter
- **THEN** the request is treated as confirm mode and the Add dialog opens

#### Scenario: Confirming the dialog adds the download with its context
- **WHEN** the user confirms a dialog opened by a confirm-mode add that carried cookies, headers and a referer
- **THEN** the download is created carrying that same context and is started subject to `start` and the queue's cap

#### Scenario: Cancelling the dialog adds nothing
- **WHEN** the user cancels a dialog opened by a confirm-mode add
- **THEN** no download is created and no download is started

#### Scenario: A confirm-mode add answers without waiting for the user
- **WHEN** a confirm-mode add request is received and the dialog stays open
- **THEN** the API has already responded `202` and the caller's request is not held open

### Requirement: Add-status endpoint
The local API SHALL expose `GET /api/add-status?ticket=…` reporting the state of a pending confirmation as one of `pending`, `added` or `cancelled`, so a caller that must know the outcome — notably the browser extension, which only cancels the browser's own download once the app is really fetching — can wait for it without holding a request open.

An `added` result SHALL carry the new item's `id` so the caller can follow it through `/api/list`. An unknown or expired ticket SHALL answer `404`. A pending confirmation the user never answers SHALL be forgotten after a bounded time and thereafter read as `cancelled` or `404`, never as `added`.

#### Scenario: Polling a pending confirmation
- **WHEN** a caller polls `/api/add-status` with a ticket whose dialog is still open
- **THEN** the API responds `200` with `"state":"pending"`

#### Scenario: Polling a confirmed add
- **WHEN** the user confirms the dialog and the caller polls the ticket
- **THEN** the API responds `200` with `"state":"added"` and the new item's `id`

#### Scenario: Polling a cancelled add
- **WHEN** the user cancels the dialog and the caller polls the ticket
- **THEN** the API responds `200` with `"state":"cancelled"` and no `id`

#### Scenario: An unknown ticket is not a pending one
- **WHEN** a caller polls `/api/add-status` with a ticket the app does not know
- **THEN** the API responds `404` and never reports it as added

### Requirement: The CLI add path is never held behind a dialog
A download handed to the app through the CLI add payload SHALL be added silently regardless of the `confirm` parameter and of the "ask before adding programmatic downloads" setting, because a script cannot answer a modal and must not be left waiting on one.

#### Scenario: A CLI add ignores the confirm setting
- **WHEN** the "ask before adding programmatic downloads" setting is on and the app is handed a CLI add payload
- **THEN** the download is added silently with no dialog
