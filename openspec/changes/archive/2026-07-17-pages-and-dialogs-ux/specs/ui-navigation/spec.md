# ui-navigation Specification (delta)

## ADDED Requirements

### Requirement: Page titles stay pinned while scrolling
On every page, the page title SHALL remain visible at the top while the page content scrolls, so the user always sees which page they are on.

#### Scenario: Title stays visible on a long page
- **WHEN** the user scrolls a long page toward the bottom
- **THEN** the page title remains pinned at the top of the page

### Requirement: New schedules get distinct numbered names
A newly-created schedule SHALL be named with a number (e.g. "Schedule 1", "Schedule 2") that is distinct from the "New schedule" button label, so the item and the action are not confused.

#### Scenario: First and second schedules are numbered
- **WHEN** the user creates two schedules
- **THEN** they are named "Schedule 1" and "Schedule 2" (not the same text as the create button)
