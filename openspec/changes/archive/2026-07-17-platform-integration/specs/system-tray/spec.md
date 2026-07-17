# system-tray Specification (delta)

## ADDED Requirements

### Requirement: First tray/taskbar activation shows the main window
When the app is hidden to the tray, the first activation of its tray/taskbar icon SHALL show and foreground the main window (not require a second click).

#### Scenario: One click restores the window
- **WHEN** the app is hidden to the tray and the user activates its icon once
- **THEN** the main window is shown and brought to the foreground

### Requirement: The tray does not block the update self-swap on exit
When an update is pending and the app exits (with the tray active), the pending file swap SHALL be applied and the app SHALL relaunch — the tray keeping the process alive in the background SHALL NOT prevent the swap/restart.

#### Scenario: Update applies on exit with tray active
- **WHEN** an update is downloaded and ready and the user exits the app while the tray is active
- **THEN** the swap is applied and the updated app relaunches (or, if this cannot be verified off the target OS, a precise on-device repro/plan is recorded)
