# notifications Specification (delta)

## ADDED Requirements

### Requirement: Hidden-to-tray notifications use the OS channel

When the application is hidden in the system tray, user notifications SHALL be delivered as operating-system
notifications rather than in-app toasts, on every platform — because an in-app toast cannot be seen while no
window is on screen.

#### Scenario: A download finishes while the app is in the tray

- **WHEN** a download completes while the app is hidden in the system tray
- **THEN** the completion is shown as an OS notification, not an in-app toast

#### Scenario: Focused foreground still uses in-app toasts

- **WHEN** a notification fires while a window is on screen and focused
- **THEN** it is shown as an in-app toast
