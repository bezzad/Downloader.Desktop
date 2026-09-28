## ADDED Requirements

### Requirement: Logging is off until the user turns it on, then fully detailed
The app SHALL write no log while logging is off in Settings, which SHALL be the default. When the user
turns logging on, the app SHALL write the full detailed log described by this capability, with no
further level to choose.

#### Scenario: Off by default
- **WHEN** the app runs with logging never turned on
- **THEN** no log file is written

#### Scenario: On means detailed
- **WHEN** the user turns logging on and uses the app
- **THEN** the log contains the user's actions, setting changes and the app's reactions to them

### Requirement: The log records every user action
While logging is on, every click on a button, toolbar button, menu item or context-menu item, every
toggle of a checkbox or switch, every page change, every dialog opened and its result when closed,
and every tray icon click and tray menu choice SHALL be written to the log with the time, what was
used, where (which window or page), and on what (e.g. the download's file name). Typed text and key
strokes SHALL NOT be logged.

#### Scenario: A context-menu click is logged
- **WHEN** the user right-clicks a download and chooses Pause
- **THEN** the log has a line naming the Pause item, the downloads page and the download's file name

#### Scenario: A page change is logged
- **WHEN** the user opens the Settings page
- **THEN** the log has a line saying the Settings page was opened

#### Scenario: Typed text is not logged
- **WHEN** the user types a search word or pastes a link
- **THEN** the typed text does not appear in the log

### Requirement: Setting changes are logged with old and new values
Each change to a setting SHALL be logged once as the setting's name with its old and new value,
including changes made by Reset to defaults and by importing settings. Values of secret-like settings
and the user-name/password part of a proxy address SHALL be shown as `***`.

#### Scenario: A number setting changes
- **WHEN** the user changes the concurrent-downloads limit from 3 to 5
- **THEN** the log has a line with the setting's name and `3 → 5`

#### Scenario: A proxy password is masked
- **WHEN** the user sets the proxy to `http://user:pass@proxy:8080`
- **THEN** the log shows the proxy with `***` instead of `user:pass`

### Requirement: The log records what the app does in response
While logging is on, the log SHALL record each download's lifecycle (added, started, paused, resumed,
stopped, retried with the reason, completed, failed), queue and scheduler decisions, address
fail-over and connection back-off, plugin calls with their duration, update checks and local-API
routes. Per-second download progress SHALL NOT be logged. Each failure SHALL be logged with its full
exception details, including the stack trace.

#### Scenario: A failure has a stack trace
- **WHEN** a download fails with an exception
- **THEN** the log has the failure line with the exception type, message and stack trace

#### Scenario: Progress does not flood the log
- **WHEN** a download runs for one minute without changing state
- **THEN** the log holds no per-tick progress lines for it

### Requirement: The log never contains secrets
The log SHALL NOT contain cookies, request headers, the query part or fragment of any URL, or the
user-name/password part of any URL. URLs SHALL be written as scheme, host, port and path only.

#### Scenario: A signed link is shortened
- **WHEN** a download of `https://cdn.example.com/f.zip?token=abc` is started
- **THEN** the log shows `https://cdn.example.com/f.zip` and does not contain `token=abc`

#### Scenario: Cookies from the extension are not logged
- **WHEN** the browser extension adds a download with cookies and headers
- **THEN** none of the cookie or header values appear in the log

### Requirement: Each session starts with a header
When logging is on, each app start SHALL first write a header with the app version, operating system
and architecture, .NET runtime, language, theme and the non-secret settings.

#### Scenario: Header on start
- **WHEN** the app starts with logging on
- **THEN** the first lines of that session give the version, OS, runtime, language and theme

### Requirement: Old log files are removed after 7 days
The app SHALL delete its own log files that are older than 7 days, at startup and when the day
changes while it runs. It SHALL NOT delete any other file in the log folder.

#### Scenario: An old file is removed
- **WHEN** the log folder holds log files from 3 and 9 days ago and the app starts
- **THEN** the 9-day-old file is deleted and the 3-day-old file is kept

#### Scenario: Other files are left alone
- **WHEN** the log folder holds a file the app did not write
- **THEN** that file is not deleted

### Requirement: Export log saves every kept log file
Export log SHALL save all the kept log files (up to 7 days) as one `.zip` file at the place the user
picks, and SHALL tell the user whether it worked. When there is no log to export it SHALL say so.

#### Scenario: Export several days
- **WHEN** log files from three different days exist and the user chooses Export log and a place
- **THEN** one zip containing the three files is saved there and a success message is shown

#### Scenario: Nothing to export
- **WHEN** no log file exists and the user chooses Export log
- **THEN** the app says there is no log to export instead of doing nothing
