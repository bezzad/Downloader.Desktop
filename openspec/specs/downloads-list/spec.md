# downloads-list Specification

## Purpose

Presentation of the downloads grid rows in the main window.

## Requirements

### Requirement: Full file name is readable on hover
The downloads grid SHALL let the user read a download's complete file name by hovering the pointer over its Name cell, even when the name is too long to fit the column and is trimmed with an ellipsis.

#### Scenario: Hover reveals the full name
- **WHEN** the pointer hovers over a Name cell whose text is trimmed
- **THEN** a tooltip shows the complete file name

#### Scenario: Failed download also shows its error
- **WHEN** the pointer hovers over the Name cell of a failed download
- **THEN** the tooltip shows the full name and the failure reason
