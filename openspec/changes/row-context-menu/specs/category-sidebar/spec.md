## MODIFIED Requirements

### Requirement: Category icons are chosen from the app's own icon set
A category's icon SHALL be chosen from the icons the app ships with, identified by a stable key,
and SHALL be paired with a user-chosen color. A category SHALL NOT reference an image file on disk.

#### Scenario: Picking an icon and a color
- **WHEN** the user picks an icon and a color for a category
- **THEN** that icon in that color is shown for the category in the sidebar and beside the file name
  of each of its downloads in the grid

#### Scenario: An unrecognized icon key degrades safely
- **WHEN** a category carries an icon key this version of the app does not recognize
- **THEN** a default icon is shown, no error is raised, and the original key is preserved in the
  configuration so a later version can render it correctly
