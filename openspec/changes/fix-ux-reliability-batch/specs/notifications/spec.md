## MODIFIED Requirements

### Requirement: Focus-aware message routing
The app SHALL route every user-facing message (errors, download completion, download failure, update availability, plugin events) to exactly one channel based on application focus: in-app toasts when the app is focused, OS notifications when it is not — on every supported platform (Windows, Linux, macOS).

#### Scenario: App is focused
- **WHEN** a message is raised and any application window is active (focused)
- **THEN** the message is shown as an in-app toast
- **AND** no OS notification is sent for that message

#### Scenario: App is unfocused or minimized to tray
- **WHEN** a message is raised and no application window is active (unfocused, minimized, or running from the system tray)
- **THEN** the message is shown as an OS notification
- **AND** no in-app toast is shown for that message

#### Scenario: Unfocused on Windows uses a real OS notification
- **WHEN** a message is raised on Windows while the app is unfocused or hidden to the tray
- **THEN** the message is shown as a native Windows notification, not an in-app toast
