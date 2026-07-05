# ui-theme Specification

## Purpose

App-wide control styling conventions.
## Requirements
### Requirement: ComboBoxes have consistent inner padding
Every ComboBox in the application SHALL render its selected text and dropdown items with consistent inner padding so the text sits inside the control rather than flush against its edge.

#### Scenario: Selected text is inset
- **WHEN** a ComboBox displays its selected value
- **THEN** the text has horizontal and vertical padding inside the control border

### Requirement: Language flag icons render at high resolution
Each language option's flag icon in the Language picker SHALL be sourced from an image with high enough resolution that it renders crisply (no visible blur or blockiness) at the picker's display size on both standard and HiDPI displays.

#### Scenario: Flag renders crisply in the language picker
- **WHEN** the user opens the Language combobox in Settings
- **THEN** each flag icon appears sharp and clean at its display size
- **AND** no flag appears blurry or pixelated on a HiDPI (2x/3x scale factor) display

#### Scenario: Flag identity is preserved
- **WHEN** a flag icon is regenerated at a higher resolution
- **THEN** it still depicts the correct country/language mapping and design details

### Requirement: Modal windows are resizable
Every modal window (Add Download, the Queues/Scheduler/Settings management dialog, and the download details dialog) SHALL be user-resizable within a sensible minimum size, rather than fixed.

#### Scenario: Add Download dialog can be resized
- **WHEN** the user drags an edge or corner of the Add Download dialog
- **THEN** the window resizes accordingly, down to its minimum width/height and up to any practical limit

### Requirement: Modal windows remember their last size per window type
Each modal window type SHALL persist its last user-set size and restore it the next time a window of that type opens, independently of the other window types. The management dialog (Queues/Scheduler/Settings) SHALL share one remembered size across its pages, since they use the same window.

#### Scenario: Resized Settings dialog reopens at the same size
- **WHEN** the user resizes the Settings dialog and closes it
- **AND** later reopens the Queues dialog (same underlying window type)
- **THEN** it opens at the size last set for that window type

#### Scenario: Details dialog size is independent
- **WHEN** the user resizes the download details dialog
- **THEN** reopening the Add Download dialog or the management dialog is unaffected — each window type keeps its own remembered size

#### Scenario: Persisted size is clamped to a usable range
- **WHEN** a window's persisted size would be smaller than its minimum size, or larger than the current screen's working area
- **THEN** the window opens clamped to a valid, on-screen, usable size instead of the raw stored value

