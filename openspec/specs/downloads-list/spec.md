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

### Requirement: A Stopped/Paused filter lists interrupted downloads
The footer filters SHALL include a Stopped bucket that matches items in the Paused or Stopped state, with a count of exactly those items. The filter buckets (All, Active, Queued, Completed, Stopped, Failed) SHALL be mutually disjoint and jointly cover every item, so a user can always find paused/stopped downloads after a restart.

#### Scenario: Paused downloads are visible after restart
- **WHEN** the user paused downloads before closing, and reopens the app (interrupted items load as Stopped)
- **THEN** selecting the Stopped filter lists those paused/stopped items and its count equals their number

#### Scenario: Buckets are disjoint and exhaustive
- **WHEN** the list contains a mix of Running, Paused, Stopped, Queued, Completed and Failed items
- **THEN** each item matches exactly one of the non-All buckets and the bucket counts sum to the total item count

### Requirement: Total downloaded size shown in the status bar
The main-window status bar SHALL display the cumulative downloaded size across all items (human-readable) next to the total speed, updated live.

#### Scenario: Total downloaded reflects the sum
- **WHEN** several downloads have downloaded bytes
- **THEN** the status bar shows the sum of their downloaded bytes as a human-readable size beside the speed, and it updates as bytes arrive
