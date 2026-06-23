# plugins Specification (delta)

## ADDED Requirements

### Requirement: A plugin can be removed

The Plugins page SHALL let the user remove an installed plugin; removing it SHALL stop the plugin from
contributing immediately and SHALL delete its file from the plugins folder so it does not load again on the
next launch.

#### Scenario: Removing a plugin uninstalls it

- **WHEN** the user clicks Remove on an installed plugin
- **THEN** the plugin disappears from the Plugins list
- **AND** it no longer contributes resolvers/providers
- **AND** its file is deleted so it does not reappear after restarting the app
