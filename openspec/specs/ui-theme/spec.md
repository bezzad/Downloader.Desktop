# ui-theme Specification

## Purpose

App-wide control styling conventions.

## Requirements

### Requirement: ComboBoxes have consistent inner padding
Every ComboBox in the application SHALL render its selected text and dropdown items with consistent inner padding so the text sits inside the control rather than flush against its edge.

#### Scenario: Selected text is inset
- **WHEN** a ComboBox displays its selected value
- **THEN** the text has horizontal and vertical padding inside the control border
