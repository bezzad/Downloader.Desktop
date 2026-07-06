## ADDED Requirements

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
