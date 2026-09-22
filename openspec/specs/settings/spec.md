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

### Requirement: Categories and the sidebar state are part of the configuration
The persisted configuration SHALL hold the category list — each category's identifier, name, icon
key, color, extension list and position — and whether the category sidebar is shown. Both SHALL be
saved when they change and restored on the next launch.

#### Scenario: A new category survives a restart
- **WHEN** the user creates a category, quits the app and starts it again
- **THEN** the category is listed with the name, icon, color, extensions and position it was given

#### Scenario: A configuration written before this change is upgraded
- **WHEN** the app starts with a configuration file that has no category list
- **THEN** the built-in categories are created, the sidebar is hidden, and the configuration is saved
  in the new form without losing any existing setting or download

### Requirement: Settings offers export and import
The Settings page SHALL offer an action to export the user's settings and categories to a file and
an action to import them from a file.

#### Scenario: Export is reachable from Settings
- **WHEN** the user opens the Settings page
- **THEN** an export action and an import action are available

#### Scenario: Import takes effect without a restart
- **WHEN** the user imports a settings file
- **THEN** the imported settings and categories are in effect immediately

### Requirement: Removing a download can delete its partial file
Settings SHALL offer an option that makes Remove delete the download's half-finished file from disk
as well as its record. The option SHALL default to off, preserving today's behaviour. A completed
file SHALL never be deleted by Remove, whatever the option's value.

#### Scenario: Option off leaves the partial file alone
- **WHEN** the option is off and the user removes an unfinished download
- **THEN** the record is removed and the partial file is left on disk

#### Scenario: Option on deletes the partial file
- **WHEN** the option is on and the user removes an unfinished download
- **THEN** the record is removed and the partial file is deleted from disk

#### Scenario: A completed file is never deleted
- **WHEN** the option is on and the user removes a completed download
- **THEN** the record is removed and the downloaded file remains on disk
