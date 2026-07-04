# browser-extension Specification

## Purpose

The companion browser extension's capture behavior: silent add through the local API by default, with a user-selectable dialog mode and graceful fallback on older app versions.
## Requirements
### Requirement: Silent add via the local API
The browser extension SHALL send captured links to the app's silent `/api/add` endpoint by default (adding and auto-starting without opening a dialog), and SHALL fall back to the legacy `/add?url=` dialog endpoint when the user selects the dialog option or when `/api/add` is unavailable on an older app version.

#### Scenario: Capture adds silently
- **WHEN** the user captures a link with the extension and the silent option is selected
- **THEN** the extension calls `/api/add` and the app adds and starts the download without showing a dialog

#### Scenario: Fallback on older app
- **WHEN** the extension calls `/api/add` but the running app does not implement it (404)
- **THEN** the extension retries with the legacy `/add?url=` endpoint so capture still works

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

