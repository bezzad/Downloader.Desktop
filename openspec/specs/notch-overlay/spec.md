# notch-overlay Specification

## Purpose
TBD - created by archiving change add-dynamic-island-notch. Update Purpose after archive.
## Requirements
### Requirement: Optional top-center notch overlay
The app SHALL offer an opt-in always-on-top overlay docked at the top-center of the primary screen
(a "dynamic island"), controlled by a Settings toggle (default off), created at startup when enabled
and starting/stopping live when toggled — including while the main window is hidden in the tray.

#### Scenario: Enabling the overlay
- **WHEN** the user turns on the notch overlay in Settings
- **THEN** a slim pill appears at the top-center of the primary screen without stealing focus
- **AND** turning the toggle off removes it immediately

### Requirement: Collapsed pill shows glanceable status
While collapsed, the overlay SHALL show the system time on Windows/Linux (a minimal notch-hugging
pill on macOS) and a small live aggregate download indicator when downloads are active.

#### Scenario: Clock with active downloads
- **WHEN** downloads are running and the overlay is collapsed (Windows/Linux)
- **THEN** the pill shows the current time and a compact total download speed

### Requirement: Hover expands to live download data
Hovering the overlay SHALL expand it into a compact panel listing active downloads (name, progress,
speed) with totals; moving the mouse away collapses it; clicking surfaces the main window.

#### Scenario: Hover to inspect, click to open
- **WHEN** the user hovers the pill
- **THEN** it expands and shows the running downloads' live progress and speeds
- **AND** clicking it brings the main Downloader window to the front
- **AND** moving the pointer away collapses it back to the pill

