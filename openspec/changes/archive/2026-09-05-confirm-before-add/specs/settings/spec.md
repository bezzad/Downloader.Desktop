## ADDED Requirements

### Requirement: Ask before adding programmatic downloads
Settings SHALL offer a toggle that makes the app open the Add dialog for downloads handed to it through the local API, instead of adding them silently. It SHALL default to off, so existing scripts and integrations keep today's behaviour, and its state SHALL persist across restarts.

The setting SHALL be described in terms the user can act on: it governs downloads sent by the browser extension and by other tools using the app's local API, and it does not affect downloads added from the app's own Add dialog or the command line.

#### Scenario: Default is off
- **WHEN** the app is started with no saved value for the setting
- **THEN** the toggle reads off and programmatic adds are silent

#### Scenario: Turning it on makes programmatic adds ask
- **WHEN** the user turns the toggle on and a tool then calls the local API's add endpoint with no explicit confirm parameter
- **THEN** the Add dialog opens pre-filled instead of the download being added silently

#### Scenario: The setting survives a restart
- **WHEN** the user turns the toggle on and restarts the app
- **THEN** the toggle is still on
