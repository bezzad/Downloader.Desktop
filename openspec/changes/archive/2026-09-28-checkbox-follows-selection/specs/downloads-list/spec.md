## ADDED Requirements

### Requirement: A row's checkbox is its selection
Each downloads-grid row's checkbox SHALL always show whether that row is selected; there SHALL be no
separate "highlighted but unchecked" or "checked but not highlighted" state. A plain click on a row
SHALL select only that row. A Ctrl+click (Cmd+click on macOS) SHALL toggle that row and keep the rest
of the selection. A Shift+click SHALL select the range from the last clicked row. Clicking a row's
checkbox SHALL toggle only that row and keep the rest of the selection. The select-all checkbox SHALL
select or unselect all visible rows. A row hidden by a filter SHALL NOT remain selected. Every action
that works on "the selected downloads" SHALL act on exactly the checked rows.

#### Scenario: A click selects one row
- **WHEN** row A is checked and the user clicks row B
- **THEN** row B is checked and selected and row A is unchecked and unselected

#### Scenario: Ctrl+click adds a row
- **WHEN** row A is selected and the user Ctrl+clicks row B
- **THEN** rows A and B are both checked and selected

#### Scenario: Ctrl+click removes a row
- **WHEN** rows A and B are selected and the user Ctrl+clicks row B
- **THEN** only row A is checked and selected

#### Scenario: Shift+click selects a range
- **WHEN** the user clicks row 2 and then Shift+clicks row 5
- **THEN** rows 2 to 5 are checked and selected and no other row is

#### Scenario: Clicking a checkbox keeps the others
- **WHEN** row A is selected and the user clicks row B's checkbox
- **THEN** rows A and B are both checked and selected

#### Scenario: Actions follow the checkboxes
- **WHEN** exactly rows A and B are checked and the user clicks the toolbar Pause button
- **THEN** A and B are paused and no other download is

#### Scenario: A filtered-out row is unselected
- **WHEN** rows A and B are selected and a filter hides row B
- **THEN** only row A is selected, and clearing the filter shows row B unchecked
