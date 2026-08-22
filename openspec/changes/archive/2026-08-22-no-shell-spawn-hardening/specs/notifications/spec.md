# notifications Specification (delta)

## MODIFIED Requirements

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
