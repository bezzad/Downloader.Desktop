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
