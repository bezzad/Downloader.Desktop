# system-tray Specification

## Purpose
Reliable tray integration: the app parks in the tray without ever stranding the user or blocking updates,
and its tray menu (app-drawn on Linux/Windows, native on macOS) offers the main entry points in the app's
language.

## Requirements


### Requirement: First tray/taskbar activation shows the main window
When the app is hidden to the tray, the first activation of its tray icon SHALL open the tray menu
(not require a second click), and choosing its first item, "Show downloads", SHALL show and foreground
the main window on the downloads list.

#### Scenario: One click opens the menu, one choice restores the window
- **WHEN** the app is hidden to the tray, the user clicks its icon once and chooses "Show downloads"
- **THEN** the main window is shown, brought to the foreground and shows the downloads list

### Requirement: The tray does not block the update self-swap on exit
When an update is pending and the app exits (with the tray active), the pending file swap SHALL be applied and the app SHALL relaunch — the tray keeping the process alive in the background SHALL NOT prevent the swap/restart.

#### Scenario: Update applies on exit with tray active
- **WHEN** an update is downloaded and ready and the user exits the app while the tray is active
- **THEN** the swap is applied and the updated app relaunches (or, if this cannot be verified off the target OS, a precise on-device repro/plan is recorded)

### Requirement: The tray menu is drawn by the app on Linux and Windows
On Linux and Windows, a left click on the tray icon SHALL open a menu drawn by the app: a rounded
card near the tray area with one row per item, each with an icon and a label, a hover highlight, and
the app's current light/dark theme and accent color. The menu SHALL close when it loses focus, when Esc
is pressed, or after an item is chosen. A right click SHALL show the native menu with the same items
(until it is confirmed on device that a right click reaches the app without one). On macOS the tray
menu SHALL remain the native menu-bar menu with the same items.

#### Scenario: Left click opens the themed menu
- **WHEN** the user left-clicks the tray icon on Linux or Windows
- **THEN** the app's menu opens near the tray area in the current theme

#### Scenario: The left click works inside the snap
- **WHEN** the app runs as a snap on Ubuntu GNOME, where the native menu is blocked by the sandbox
- **THEN** a left click on the tray icon still opens the app's menu

#### Scenario: Right click shows the same items
- **WHEN** the user right-clicks the tray icon where the native menu can be shown
- **THEN** a menu with Show downloads, Settings, Notifications and Exit opens, in the app language

#### Scenario: Clicking elsewhere closes it
- **WHEN** the tray menu is open and the user clicks outside it
- **THEN** the menu closes and nothing else happens

#### Scenario: macOS keeps the native menu
- **WHEN** the user clicks the menu-bar icon on macOS
- **THEN** the native menu opens with the same items

### Requirement: The tray menu offers the main entry points
The tray menu SHALL offer, in order: Show downloads, Settings, a Notifications on/off switch, and Exit.
Show downloads SHALL bring the main window back on the downloads list; Settings SHALL bring it back on
the Settings page; the switch SHALL turn notifications on or off and the choice SHALL be saved; Exit
SHALL quit the app the same way as today, so a pending update is still applied on exit.

#### Scenario: Settings from the tray
- **WHEN** the app is hidden to the tray and the user chooses Settings in the tray menu
- **THEN** the main window is shown on the Settings page

#### Scenario: Notifications switch
- **WHEN** notifications are on and the user turns the switch off in the tray menu
- **THEN** no download notification is shown afterwards, and the setting is still off after a restart

#### Scenario: Exit quits and applies a pending update
- **WHEN** an update is ready and the user chooses Exit in the tray menu
- **THEN** the app quits and the update is applied

### Requirement: Tray labels follow the app language
Every tray menu label SHALL be shown in the app's current language and SHALL change when the user
switches language, without a restart.

#### Scenario: Persian labels
- **WHEN** the app language is Persian
- **THEN** the tray menu labels are in Persian and the menu is laid out right-to-left
