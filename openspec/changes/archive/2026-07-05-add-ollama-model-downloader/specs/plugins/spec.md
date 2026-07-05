# plugins Specification (delta)

## ADDED Requirements

### Requirement: Built-in plugins ship with the app

The application SHALL bundle first-party plugins (GitHub Releases, Ollama Models) in its install
directory's `plugins/` folder and load them at startup as **built-in** plugins. Built-in plugins SHALL be
present and enabled after first install, SHALL be updated with the app, and SHALL NOT be removable —
Settings offers only an enable/disable toggle for them. Per-plugin enabled state SHALL persist in the app
config across restarts and app updates.

#### Scenario: Fresh install has the built-ins
- **WHEN** the app runs for the first time
- **THEN** GitHub Releases and Ollama Models appear in Settings → Plugins, enabled

#### Scenario: Built-ins cannot be removed, only disabled
- **WHEN** the user opens a built-in plugin's entry in Settings → Plugins
- **THEN** there is no Remove action, only an enable/disable toggle
- **AND** a disabled built-in stays disabled after restarting and after updating the app

#### Scenario: User-installed plugins keep removable behavior
- **WHEN** the user installs a plugin DLL into the user plugins folder (e.g. the HLS plugin)
- **THEN** it can still be removed from Settings → Plugins as before

### Requirement: A plugin can offer a post-download action

The plugin SDK SHALL let a plugin register a named post-download action (label + can-offer check +
execute). For a completed download that was resolved by that plugin, the host SHALL surface the action to
the user (on the completion notification and on the item), SHALL run it only when the user triggers it,
and SHALL show the action's failure message on error. Which plugin resolved a download SHALL be recorded
on the persisted item so offers survive restarts.

#### Scenario: Action appears only on that plugin's completed downloads
- **WHEN** a download resolved by a plugin with a registered post-download action completes
- **THEN** the action's label is offered on the completed item
- **AND** downloads not resolved by that plugin do not show the action

#### Scenario: Action runs only on user click
- **WHEN** the download completes and the user does nothing
- **THEN** the action is not executed

#### Scenario: Action failure is shown, download stays intact
- **WHEN** an executed action throws
- **THEN** its message is shown like other friendly item errors
- **AND** the downloaded file and item state are unchanged

## REMOVED Requirements

### Requirement: Bundled sample plugin implements the current SDK
<!-- Superseded by "Built-in plugins ship with the app": the sample project becomes the built-in
     GitHub Releases plugin (same plugin id) under src/Downloader.Desktop.Plugins/. -->

## MODIFIED Requirements

### Requirement: A plugin can be removed
User-installed plugins (loaded from the user plugins folder) SHALL be removable from Settings → Plugins,
unloading them and deleting their files. Built-in plugins SHALL NOT be removable (see "Built-in plugins
ship with the app").

#### Scenario: Removing a user-installed plugin deletes it
- **WHEN** the user removes a user-installed plugin
- **THEN** it disappears from the list and its DLL is deleted from the user plugins folder

#### Scenario: Built-ins offer no removal
- **WHEN** the user views a built-in plugin
- **THEN** no Remove action is available
