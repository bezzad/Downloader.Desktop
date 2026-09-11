## ADDED Requirements

### Requirement: The scheduler timer only runs when something is scheduled
The download manager SHALL NOT run its periodic scheduler timer while there is nothing to schedule, and
SHALL start it when a schedule appears. The timer SHALL be stopped and released when the manager is
disposed, so a manager that goes out of scope leaves no live timer behind on the dispatcher.

#### Scenario: No schedules means no timer
- **WHEN** the manager initializes from a configuration whose schedule list is empty
- **THEN** no periodic scheduler timer is running

#### Scenario: Adding the first schedule starts the timer
- **WHEN** a schedule is added to a manager that had none
- **THEN** the periodic scheduler timer is running and evaluates that schedule

#### Scenario: Disposing the manager releases the timer
- **WHEN** a manager whose scheduler timer is running is disposed
- **THEN** the timer is stopped and no further schedule evaluation occurs for that manager

#### Scenario: Many short-lived managers leave no timers behind
- **WHEN** many managers are created, initialized and disposed within one dispatcher's lifetime
- **THEN** the number of live scheduler timers on that dispatcher does not grow with the number of managers

### Requirement: A cancelled shutdown countdown is stopped, not just hidden
When an automatic shutdown is cancelled, the app SHALL stop the countdown itself — not merely dismiss its
dialog. No power-off command SHALL be issued after a cancellation, however long the process keeps running.

#### Scenario: Cancelling from outside the dialog really cancels
- **WHEN** a shutdown countdown is armed and then cancelled through the service (the tray's "cancel
  shutdown", not the dialog's own button)
- **THEN** the countdown stops and no platform power-off command is ever issued, even after more than the
  countdown's duration has elapsed

#### Scenario: A dismissed dialog leaves no timer running
- **WHEN** the countdown dialog is closed by anything other than reaching zero
- **THEN** its timer is stopped and released, so it cannot fire later on a dispatcher that outlives it
