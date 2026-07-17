# settings Specification (delta)

## ADDED Requirements

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
