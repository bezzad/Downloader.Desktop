# taskbar-progress Specification

## Purpose
The OS taskbar/dock reflects the overall download progress where the platform supports it.

## Requirements

### Requirement: OS taskbar/dock shows aggregate download progress
When downloads are active, the app SHALL report overall progress (an aggregate of active downloads, 0–100%) to the OS taskbar/dock where the platform supports it (Windows taskbar progress; Linux Unity LauncherEntry-capable docks; macOS Dock where available), and SHALL clear it when idle. On platforms without support it SHALL be a no-op.

#### Scenario: Progress shown while downloading
- **WHEN** one or more downloads are active
- **THEN** the taskbar/dock reflects the aggregate progress, updating as it changes

#### Scenario: Progress cleared when idle
- **WHEN** no downloads are active
- **THEN** the taskbar/dock progress indicator is cleared
