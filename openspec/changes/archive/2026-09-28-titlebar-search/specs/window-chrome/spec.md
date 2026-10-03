## ADDED Requirements

### Requirement: The main window's title is centered in its title bar
The main window's title bar SHALL show the app title centered horizontally in the window. When the
title-bar controls on the right leave no room for it, the title SHALL be hidden rather than drawn
under those controls. Dialog title bars SHALL keep their left-aligned title. The app SHALL NOT show a
menu bar (File, Tasks, Help, …).

#### Scenario: Title is centered
- **WHEN** the main window is shown at its default size
- **THEN** the title "Downloader" is centered horizontally in the title bar

#### Scenario: Title never overlaps the controls
- **WHEN** the main window is at its minimum width and the search box is expanded
- **THEN** the title is either fully visible without touching the right-side controls, or hidden

#### Scenario: Dialogs are unchanged
- **WHEN** the Add-link or Details dialog is open
- **THEN** its title bar shows its title on the left and has no search box

### Requirement: The search box lives in the main window's title bar
The downloads search box SHALL be placed in the main window's title bar, immediately left of the
window buttons, and SHALL filter the downloads list exactly as before. The Update, Donate and About
buttons SHALL stay at the right end of the top bar below the title bar (author's decision after review). Clicking
the search box or a title-bar button SHALL NOT start a window drag; empty title-bar space SHALL still
drag the window and double-clicking it SHALL still toggle maximize.

#### Scenario: Search from the title bar
- **WHEN** the user types a word into the title-bar search box
- **THEN** the downloads list shows only the matching downloads

#### Scenario: App buttons stay in the top bar
- **WHEN** the main window is shown
- **THEN** the Donate and About buttons (and Update, when an update is ready) are in the top bar
  and not in the title bar

#### Scenario: Clicking the search box does not drag the window
- **WHEN** the user presses the mouse inside the search box
- **THEN** the text cursor is placed in the box and the window does not move

### Requirement: The search box grows while it is in use
The title-bar search box SHALL widen with a short animation when it receives keyboard focus, and SHALL
return to its normal width with the same animation when it loses focus while empty. While it contains
text it SHALL stay at the wider width.

#### Scenario: Focus widens the box
- **WHEN** the user clicks into the empty search box
- **THEN** it animates to a wider width

#### Scenario: Leaving an empty box narrows it
- **WHEN** the search box is empty and focus moves elsewhere
- **THEN** it animates back to its normal width

#### Scenario: A box with text stays wide
- **WHEN** the search box contains text and focus moves elsewhere
- **THEN** it keeps the wider width
