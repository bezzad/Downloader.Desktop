# notifications Specification

## Purpose

How user-facing messages (errors, download completion/failure, update availability, plugin events) are routed to a channel (focus-aware: in-app vs OS) and presented (copyable content, actionable re-show on focus).

## Requirements

### Requirement: Focus-aware message routing
The app SHALL route every user-facing message (errors, download completion, download failure, update availability, plugin events) to exactly one channel based on application focus: in-app toasts when the app is focused, OS notifications when it is not.

#### Scenario: App is focused
- **WHEN** a message is raised and any application window is active (focused)
- **THEN** the message is shown as an in-app toast
- **AND** no OS notification is sent for that message

#### Scenario: App is unfocused or minimized to tray
- **WHEN** a message is raised and no application window is active (unfocused, minimized, or running from the system tray)
- **THEN** the message is shown as an OS notification
- **AND** no in-app toast is shown for that message

### Requirement: Actionable notification re-show on focus
The app SHALL preserve the action of an actionable message (e.g. "Update available") when it is raised while unfocused, by sending a plain OS notification then re-showing the clickable in-app toast once the app regains focus.

#### Scenario: Update available while unfocused
- **WHEN** an actionable message is raised while the app is unfocused
- **THEN** a plain OS notification is sent immediately
- **AND** when the app next becomes focused, the actionable in-app toast is shown so its action is not lost

#### Scenario: Actionable message while focused
- **WHEN** an actionable message is raised while the app is focused
- **THEN** the actionable in-app toast is shown immediately and is not duplicated on later focus changes

### Requirement: Copyable toast content
Every in-app toast SHALL expose a control to copy its text (title and message) to the clipboard.

#### Scenario: User copies a toast
- **WHEN** an in-app toast is shown and the user activates its copy control
- **THEN** the toast's title and message text are placed on the system clipboard
