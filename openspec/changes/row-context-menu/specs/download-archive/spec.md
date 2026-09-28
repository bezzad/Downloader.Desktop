## MODIFIED Requirements

### Requirement: Archive and Restore are reachable per row and in bulk
Each download row's right-click menu SHALL offer an Archive action, shown as Restore while the
archived view is active. The toolbar SHALL offer an Archive button next to Remove, enabled by the
current selection in the same way as Start, Pause and Stop. While the Archived filter is active the
toolbar's download actions SHALL be Restore and Remove only.

#### Scenario: Row menu archives a single download
- **WHEN** the user right-clicks a download row and chooses Archive
- **THEN** that download is archived and leaves the list

#### Scenario: Toolbar archives the selected downloads
- **WHEN** several downloads are selected and the user clicks the toolbar Archive button
- **THEN** every selected download is archived

#### Scenario: Toolbar switches to Restore in the archived view
- **WHEN** the Archived filter is selected
- **THEN** the toolbar shows Restore and Remove instead of Start, Pause, Stop and Archive

#### Scenario: Archive is disabled with no selection
- **WHEN** no download is selected
- **THEN** the toolbar Archive button is visible but disabled, exactly as Start, Pause and Stop are
