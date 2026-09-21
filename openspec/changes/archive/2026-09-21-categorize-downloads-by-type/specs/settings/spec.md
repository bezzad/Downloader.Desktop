## ADDED Requirements

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
