# window-chrome Specification

## Purpose
Behavior of the custom (borderless) window chrome: dragging, resizing, and keeping the window on-screen.
## Requirements
### Requirement: Manual edge/corner resize keeps the window on-screen
Dragging a custom-chrome (borderless) window's resize edge or corner SHALL keep the window fully visible
and its size within its configured minimum/maximum bounds, regardless of which edge is dragged, the drag
speed, or the number of pointer-move events received during the drag.

#### Scenario: Resizing from the right or bottom edge
- **WHEN** the user drags the right or bottom edge of a resizable window
- **THEN** the window's width or height changes accordingly
- **AND** the window's position and visibility are unaffected

#### Scenario: Resizing from the left or top edge
- **WHEN** the user drags the left or top edge of a resizable window, including with fast pointer movement
- **THEN** the window's size changes and its position shifts to keep the opposite edge fixed
- **AND** the window remains fully visible on screen throughout and after the drag

#### Scenario: Rapid dragging does not accumulate position error
- **WHEN** the user drags a left or top edge with many rapid pointer-move events
- **THEN** the window's final position and size reflect the actual drag distance
- **AND** the window never ends up off-screen or invisible as a result of the drag

### Requirement: Modal dialogs are visually distinct from the main window
Every modal dialog SHALL have a border/background/elevation clearly distinct from the main window (e.g. an accent border plus elevation), so the user can immediately tell a modal is open over the disabled main window.

#### Scenario: An open modal is visually distinguishable
- **WHEN** a modal dialog (e.g. About) is open over the main window
- **THEN** the modal has a distinct border/background/elevation from the main window, making it obvious a foreground dialog is active

### Requirement: Modal chrome is refined and corner-true
Modal dialogs SHALL use a 1px accent border and their inner content SHALL be clipped to the rounded corners so no square edge overhangs the arc.

#### Scenario: Top corners match bottom corners
- **WHEN** any modal is open
- **THEN** all four corners render the same rounded arc with no square content poking through

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
