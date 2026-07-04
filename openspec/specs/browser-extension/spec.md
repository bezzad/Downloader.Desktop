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
