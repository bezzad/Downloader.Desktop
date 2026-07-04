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
