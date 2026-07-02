# cli Specification

## Purpose

Command-line verbs on the main binary so scripts and terminals can submit and control downloads (issue #2): `add`, `list`, and `pause|resume|cancel|retry|remove <id>`.

## Requirements

### Requirement: CLI add verb
The main binary SHALL accept `add --url <url> [--filename <name>] [--path <folder>] [--queue <name>] [--no-start]` and submit the download silently to the running instance, or start the app (detached) and perform the add at startup when no instance is running. The invoking process SHALL exit promptly (never block on the GUI) with exit code 0 on success.

#### Scenario: Add while the app is running
- **WHEN** the user runs `downloader add --url https://example.com/file.zip` and an instance is running
- **THEN** the running instance adds and starts the download silently (no dialog)
- **AND** the CLI process prints a confirmation and exits 0

#### Scenario: Add while the app is not running
- **WHEN** the user runs the add verb and no instance is running
- **THEN** the app is launched detached, performs the silent add at startup
- **AND** the CLI invocation exits 0 without waiting for the GUI to close

#### Scenario: Bare-URL launch keeps today's behavior
- **WHEN** the app is launched with a plain http(s) URL argument and no CLI verb
- **THEN** the existing behavior is preserved (the Add dialog opens pre-filled)

### Requirement: CLI list and control verbs
The main binary SHALL accept `list` (prints the `/api/list` JSON array to stdout) and `pause|resume|cancel|retry|remove <id>` verbs that call the local API on the running instance. When the app is not running or the integration toggle is off, these verbs SHALL print a one-line friendly error and exit 1.

#### Scenario: List downloads from a script
- **WHEN** the user runs `downloader list` while the app is running with the toggle on
- **THEN** stdout receives the JSON array of downloads and the process exits 0

#### Scenario: Control verb without a running app
- **WHEN** the user runs `downloader pause <id>` and the app is not running
- **THEN** the CLI prints a friendly error explaining the app must be running with integration enabled and exits 1

#### Scenario: Usage error
- **WHEN** the user runs a known verb with missing/invalid arguments (e.g. `add` without `--url`)
- **THEN** the CLI prints usage help and exits 2

### Requirement: CLI output is visible on all platforms
CLI verb output SHALL be visible when invoked from a terminal on Windows, Linux and macOS (on Windows the GUI-subsystem binary attaches to the parent console before printing).

#### Scenario: Windows terminal shows output
- **WHEN** the user runs a CLI verb from cmd/PowerShell on Windows
- **THEN** the confirmation/error/JSON output appears in that terminal
