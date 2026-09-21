## ADDED Requirements

### Requirement: Settings and categories can be exported to a file
The app SHALL let the user export their settings and their categories to a single file of their
choosing. The export SHALL contain the app settings and the category list, and SHALL NOT contain the
download list, the queues, or the schedules.

#### Scenario: Export writes settings and categories
- **WHEN** the user exports their settings
- **THEN** the written file contains the app settings and every category with its name, icon key,
  color, extensions and position

#### Scenario: Download data is not exported
- **WHEN** the user has downloads, queues and schedules and exports their settings
- **THEN** none of those appear in the exported file

#### Scenario: Cancelling the file picker exports nothing
- **WHEN** the user starts an export and cancels the file picker
- **THEN** no file is written and no error is shown

### Requirement: An export is platform-independent
An exported file SHALL contain no file-system paths and no values that are only meaningful on the
machine that produced it, so that a file exported on one operating system imports correctly on
another.

#### Scenario: A Linux export imports on Windows
- **WHEN** a file exported on Linux is imported on Windows
- **THEN** every category keeps its name, icon, color, extensions and position, and the icons render
  correctly

#### Scenario: Machine-specific settings are excluded
- **WHEN** the user exports their settings
- **THEN** the save folder, the resolved local API port and the remembered window sizes are not
  included

### Requirement: Settings and categories can be imported from a file
The app SHALL let the user import a previously exported file, replacing their current settings and
category list with the file's contents and applying them without requiring a restart.

#### Scenario: Import applies the settings
- **WHEN** the user imports a file exported from another machine
- **THEN** the imported settings take effect and the imported categories appear in the sidebar

#### Scenario: Imported categories re-categorize the existing downloads
- **WHEN** the user imports categories that claim extensions differently from the current ones
- **THEN** existing downloads that have no explicit category override resolve against the imported
  categories

#### Scenario: A download's own category override survives an import
- **WHEN** the user has overridden a download's category and then imports a settings file
- **THEN** that download keeps its override if the chosen category still exists

### Requirement: An import never leaves the app in a broken state
An import SHALL validate the file before applying anything. A file that is not a valid export, or
that is corrupt, SHALL be rejected with a message, leaving the current settings and categories
untouched. A valid file that is missing optional values SHALL be accepted, with defaults applied.

#### Scenario: A corrupt file is rejected
- **WHEN** the user imports a file that is not valid
- **THEN** the current settings and categories are unchanged and the user is told the file could not
  be read

#### Scenario: A file from a newer version is handled
- **WHEN** the user imports a file containing values this version of the app does not recognize
- **THEN** the recognized values are applied, the unrecognized ones are ignored, and no error is
  raised

#### Scenario: An import with no categories is refused
- **WHEN** the user imports a file whose category list is empty
- **THEN** the import is refused, so the user cannot end up with no categories at all
