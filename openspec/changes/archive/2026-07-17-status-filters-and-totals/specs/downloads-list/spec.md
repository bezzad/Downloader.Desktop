# downloads-list Specification (delta)

## ADDED Requirements

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
