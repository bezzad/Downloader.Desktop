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
