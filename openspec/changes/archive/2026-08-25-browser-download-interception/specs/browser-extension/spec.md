## MODIFIED Requirements

### Requirement: Silent add via the local API
The browser extension SHALL send captured links to the app's silent `/api/add` endpoint by default (adding and auto-starting without opening a dialog), and SHALL fall back to the legacy `/add?url=` dialog endpoint when the user selects the dialog option or when `/api/add` is unavailable on an older app version.

Every hand-off SHALL carry the context the link was found in — the live session cookies for the target URL, the originating page's referer, and any request headers needed to fetch it — so a link that the browser could fetch does not fail once the app takes it over. Capturing that context SHALL be best-effort: a failure to gather any part of it SHALL NOT prevent the link from being sent.

#### Scenario: Capture adds silently
- **WHEN** the user captures a link with the extension and the silent option is selected
- **THEN** the extension calls `/api/add` and the app adds and starts the download without showing a dialog

#### Scenario: Fallback on older app
- **WHEN** the extension calls `/api/add` but the running app does not implement it (404)
- **THEN** the extension retries with the legacy `/add?url=` endpoint so capture still works

#### Scenario: A hand-off carries the page's referer
- **WHEN** the extension sends a link that was found on a page
- **THEN** the request to `/api/add` includes that page as the referer

#### Scenario: Context capture failure does not block the send
- **WHEN** the extension cannot read cookies or determine the referer for a link
- **THEN** the link is still sent, with whatever context was available
