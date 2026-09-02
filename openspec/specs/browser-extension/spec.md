# browser-extension Specification

## Purpose

The companion browser extension's capture behavior: silent add through the local API by default, with a user-selectable dialog mode and graceful fallback on older app versions.
## Requirements
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

### Requirement: Popup silent-vs-dialog toggle and suggested filename
The extension popup SHALL provide a toggle to choose between adding silently and opening the Add dialog, persist that choice, and forward a page-provided suggested filename to `/api/add` when one is available. The popup SHALL continue to show whether the desktop app is reachable via `/ping`.

#### Scenario: User chooses dialog mode
- **WHEN** the user sets the popup toggle to "Open dialog"
- **THEN** subsequent captures use `/add?url=` and the app opens the Add dialog pre-filled

#### Scenario: Reachability indicator
- **WHEN** the popup is opened
- **THEN** it pings the app and shows a connected/not-connected indicator

### Requirement: Extension discovers the app's port within a declared range
The browser extension SHALL be able to reach the desktop app's local API even when it is bound to a fallback port, by probing a small, pre-declared range of loopback ports (matching the app's own fallback range) instead of assuming a single fixed port.

#### Scenario: App runs on the preferred port
- **WHEN** the extension checks reachability (`/ping`) and the app is listening on the preferred port `15151`
- **THEN** the extension connects on the first probe with no added latency beyond today's behavior

#### Scenario: App fell back to another declared port
- **WHEN** the app is listening on a fallback port within the declared range (e.g. `15153`) instead of `15151`
- **THEN** the extension's probe finds it within the declared range and uses that port for subsequent requests (`/add`, `/api/add`, `/ping`)
- **AND** it remembers that port for next time so future reachability checks start from it

#### Scenario: App is not reachable on any declared port
- **WHEN** none of the declared ports respond
- **THEN** the popup shows the existing "not connected" indicator, unchanged from today's behavior when the app isn't running

### Requirement: A hand-off names the extension's configured save folder
When the extension has a download folder configured, every add it sends to the app SHALL name that
folder, so the app saves the file there without consulting its own default and without asking the
user. When no folder is configured the add SHALL omit it, leaving today's behaviour unchanged.

#### Scenario: The configured folder reaches the app
- **WHEN** the extension sends an add for a link while a download folder is configured
- **THEN** the request to `/api/add` carries that folder as the download's `path`

#### Scenario: The dialog fallback is unaffected
- **WHEN** the extension falls back to the legacy `/add?url=` dialog endpoint
- **THEN** the link is still captured, and the app's Add dialog decides the folder as it does today

### Requirement: The quality picker hands over the manifest, not the rendition

When the popup lists the qualities it parsed out of an HLS master playlist, sending one SHALL hand the
app the **master** URL plus that quality's variant id — never the rendition's own URL. A rendition of
a master that keeps its audio in a separate `#EXT-X-MEDIA` group is video-only, so sending it made the
app download a video with no sound, with no way back to the audio (a rendition's URL does not reveal
its master's).

Each listed quality SHALL keep its own rendition URL for the extension's internal purposes (size
probing, duplicate suppression, preview matching), so this affects only what is sent.

#### Scenario: A picked quality arrives as master + choice
- **WHEN** the user picks a quality on an HLS card and presses Download
- **THEN** the app receives the master playlist's URL and the id of the picked quality
- **AND** does NOT receive the rendition's URL

#### Scenario: Send-all behaves the same
- **WHEN** the user sends every detected item at once
- **THEN** each HLS card is sent as its master plus its currently selected quality

#### Scenario: A quality does not force the cookie form
- **WHEN** a send carries a quality but no cookies, headers or referer
- **THEN** the extension keeps using the plain URL form it has always used, with the quality alongside

