# settings Specification

## Purpose
Findable, well-organized app settings: searchable options in collapsible sections with sensible defaults.

## Requirements

### Requirement: Settings options are searchable
The Settings page SHALL provide a search box (positioned left of "Reset to defaults") that filters the visible options to those matching the typed term: non-matching options are hidden, sections containing a match are expanded, and the matched text is highlighted. Clearing the search restores all options and prior section state.

#### Scenario: Typing filters to matching options
- **WHEN** the user types a term that matches some option labels
- **THEN** only matching options are shown, their sections are expanded, and the matched text is highlighted

#### Scenario: Clearing search restores everything
- **WHEN** the user clears the search box
- **THEN** all options are shown again

### Requirement: Settings sections are collapsible with sensible defaults
Each Settings section SHALL be collapsible and expanded by default. The Plugins section SHALL be expanded by default.

#### Scenario: Plugins expanded on open
- **WHEN** the user opens the Settings page
- **THEN** the Plugins section is expanded, and every other section is expanded and collapsible

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
