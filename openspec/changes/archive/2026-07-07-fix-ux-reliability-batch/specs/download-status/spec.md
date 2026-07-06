## ADDED Requirements

### Requirement: Stopped state survives an app restart
A download the user explicitly stopped (individually or via Stop All) SHALL remain Stopped after the
application is closed and reopened, and SHALL NOT be automatically re-queued or started by any background
mechanism (including the scheduler re-evaluating a schedule whose window already contains the current time
of day, if that schedule already fired earlier the same day).

#### Scenario: Stop All survives a restart with no schedules configured
- **WHEN** the user stops all downloads and restarts the application, with no schedules configured
- **THEN** every previously-stopped download is still Stopped after the restart
- **AND** none of them starts downloading on their own

#### Scenario: An already-fired-today schedule does not re-fire on restart
- **WHEN** an enabled schedule's start window already fired earlier today, the user then stops all
  downloads, and restarts the application while still inside that schedule's window
- **THEN** the schedule does not re-trigger a start on the restart
- **AND** the stopped downloads remain Stopped

#### Scenario: A schedule that has not yet fired today still fires normally
- **WHEN** an enabled schedule's window contains the current time of day and it has not fired yet today
- **THEN** it fires normally (on the current tick or after a restart), starting its target queue once
