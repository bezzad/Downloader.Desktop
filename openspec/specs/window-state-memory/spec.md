# window-state-memory Specification

## Purpose
The main window reopens the way the user left it — maximized state, size and position — surviving
every exit route (window close, tray quit, OS restart/kill), and always clamped to a screen that is
actually connected so a restore can never leave the window off-screen, oversized, or too small.

## Requirements

### Requirement: The main window reopens the way it was left

The app SHALL persist the main window's maximized state together with its normal-state size and
position, and SHALL restore them the next time the main window is shown.

#### Scenario: Maximized is remembered

- **WHEN** the user maximizes the main window and later exits the app
- **THEN** the next launch shows the main window maximized

#### Scenario: A normal window keeps its size and position

- **WHEN** the user resizes and moves the main window and later exits the app
- **THEN** the next launch shows the main window at that same size and position

#### Scenario: A first run has nothing to restore

- **WHEN** the app starts with no remembered layout (fresh install, or a config file predating this
  feature)
- **THEN** the window opens at its built-in default size, centred, and no error is raised

### Requirement: The layout is recorded as it changes, not only at exit

The app SHALL update the remembered layout whenever the user resizes, moves, maximizes or restores
the main window, persisting it through the application's existing debounced save.

#### Scenario: Exiting from the system tray

- **WHEN** the user maximizes the window, closes it to the tray, and quits from the tray menu
- **THEN** the next launch shows the main window maximized

#### Scenario: The app is never told it is closing

- **WHEN** the user maximizes the window and the app is then ended without a normal shutdown (OS
  restart, sign-out, or the process being killed)
- **THEN** the next launch shows the main window maximized

#### Scenario: Maximized geometry never overwrites the restore bounds

- **WHEN** the user has a normal window of a given size and position and then maximizes it
- **THEN** the remembered normal size and position are unchanged
- **AND** restoring down — in this session or after a restart — returns the window to them

#### Scenario: The platform's own confirmation of a restore is not mistaken for a user resize

- **WHEN** the app applies a remembered layout and the platform reports the resulting resize/move
  event asynchronously, carrying the new position together with the window's pre-restore size
- **THEN** that event is recognised as the restore's own echo and is not recorded as the user's choice
- **AND** a genuine resize made by the user shortly after launch is still recorded

### Requirement: A restored layout is always usable

The app SHALL clamp a remembered layout, at the moment it is applied, to the window's minimum size
and to a screen that is actually connected, so a restored window is never off-screen, oversized or
smaller than its minimum.

#### Scenario: The screen it was on is gone

- **WHEN** the remembered position lies entirely outside every connected screen's working area (for
  example a second monitor that has since been disconnected)
- **THEN** the window is re-centred on the primary screen's working area at its clamped size

#### Scenario: The screen got smaller

- **WHEN** the remembered size is larger than the largest connected screen's working area
- **THEN** the window is sized to fit that working area

#### Scenario: A tiny or corrupt remembered size

- **WHEN** the remembered size is below the window's minimum size, or is zero, negative or not a
  finite number
- **THEN** the window opens at its minimum size, or at the built-in default when the record is
  unusable

#### Scenario: A partly off-screen window is left alone

- **WHEN** the remembered position places the window partly outside a screen but leaves its title
  bar reachable
- **THEN** the position is restored unchanged

### Requirement: A minimized window is never restored minimized

The app SHALL never record a minimized (or full-screen) window as the remembered layout — the last
normal-or-maximized layout is kept instead — and SHALL never restore the window into a minimized
state.

#### Scenario: Exiting while minimized

- **WHEN** the user minimizes the main window and exits the app
- **THEN** the next launch shows a visible window at the last non-minimized layout

#### Scenario: Starting minimized to the tray is unaffected

- **WHEN** the app is launched with `--minimized` and the system tray is enabled
- **THEN** it starts hidden in the tray as before, and the remembered layout is applied when the
  window is next shown
