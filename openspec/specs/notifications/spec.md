# notifications Specification

## Purpose

How user-facing messages (errors, download completion/failure, update availability, plugin events) are routed to a channel (focus-aware: in-app vs OS) and presented (copyable content, actionable re-show on focus).
## Requirements
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

### Requirement: OS notifications are delivered without spawning a shell

The app SHALL post native OS notifications through in-process APIs only. It SHALL NOT spawn a shell,
script host, or command interpreter to deliver a notification, because an unsigned binary spawning such
a child process is scored as malicious by behavioral antivirus engines (issue #4).

#### Scenario: Windows notification
- **WHEN** a notification is posted on Windows
- **THEN** it is delivered in-process via the shell notification API (`Shell_NotifyIconW` with
  `NIF_INFO`), surfacing as a toast and remaining in the Action Center
- **AND** no child process is created

#### Scenario: Notification severity is carried through
- **WHEN** the notification represents a failure
- **THEN** the OS is asked for the error icon (`NIIF_ERROR`) rather than the information icon

#### Scenario: Delivery failure is silent
- **WHEN** the notification API is unavailable or fails for any reason
- **THEN** the call returns false, the notification is skipped, and the app continues unaffected

### Requirement: Windows OS integration never spawns a shell

Start-menu shortcut creation, run-at-startup registration, and update extraction SHALL use in-process
APIs (`IShellLink`, `Microsoft.Win32.Registry`) or, where a process is unavoidable, an in-box executable
referenced by its absolute path. A source-level guardrail test SHALL fail the build if a shell spawn,
encoded command line, script-host COM object, or browser-data read is reintroduced into shipping source.

#### Scenario: Guardrail catches a reintroduction
- **WHEN** shipping source (app or any plugin) contains `powershell`, `pwsh`, `Expand-Archive`,
  `WScript.Shell`, `-EncodedCommand`, `cmd /c`, `--cookies-from-browser`, or a spawned `reg.exe` in
  executable code or a string literal
- **THEN** the test suite fails and names the file, line, and the in-process alternative to use

#### Scenario: Explanatory comments remain allowed
- **WHEN** a comment explains why one of those patterns must not be used
- **THEN** the guardrail does not flag it, because comments are stripped before scanning while string
  literals are still scanned
