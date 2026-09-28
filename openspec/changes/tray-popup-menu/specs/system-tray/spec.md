## MODIFIED Requirements

### Requirement: First tray/taskbar activation shows the main window
When the app is hidden to the tray, the first activation of its tray icon SHALL open the tray menu
(not require a second click), and choosing its first item, "Show downloads", SHALL show and foreground
the main window on the downloads list.

#### Scenario: One click opens the menu, one choice restores the window
- **WHEN** the app is hidden to the tray, the user clicks its icon once and chooses "Show downloads"
- **THEN** the main window is shown, brought to the foreground and shows the downloads list

## ADDED Requirements

### Requirement: The tray menu is drawn by the app on Linux and Windows
On Linux and Windows, a left or right click on the tray icon SHALL open a menu drawn by the app: a
rounded card near the tray area with one row per item, each with an icon and a label, a hover
highlight, and the app's current light/dark theme and accent color. The menu SHALL close when it loses
focus, when Esc is pressed, or after an item is chosen. On macOS the tray menu SHALL remain the native
menu-bar menu with the same items.

#### Scenario: Click opens the themed menu
- **WHEN** the user left-clicks or right-clicks the tray icon on Linux or Windows
- **THEN** the app's menu opens near the tray area in the current theme

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
