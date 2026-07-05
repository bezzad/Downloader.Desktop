## ADDED Requirements

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
