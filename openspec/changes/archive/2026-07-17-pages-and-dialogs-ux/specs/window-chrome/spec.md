# window-chrome Specification (delta)

## ADDED Requirements

### Requirement: Modal dialogs are visually distinct from the main window
Every modal dialog SHALL have a border/background/elevation clearly distinct from the main window (e.g. an accent border plus elevation), so the user can immediately tell a modal is open over the disabled main window.

#### Scenario: An open modal is visually distinguishable
- **WHEN** a modal dialog (e.g. About) is open over the main window
- **THEN** the modal has a distinct border/background/elevation from the main window, making it obvious a foreground dialog is active
