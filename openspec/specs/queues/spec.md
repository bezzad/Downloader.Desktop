# queues Specification

## Purpose

How download queues are surfaced and acted on in the main window.

## Requirements

### Requirement: Queue action menus reflect the live queue set
The "Start queue" and "Stop queue" toolbar dropdowns in the main downloads view SHALL list every queue that currently exists, updating immediately when a queue is added or removed — without requiring an application restart.

#### Scenario: New queue appears in the menus immediately
- **WHEN** the user adds a new queue from the Queues page
- **THEN** the new queue appears as an entry in both the "Start queue" and "Stop queue" toolbar dropdowns without closing and reopening the app

#### Scenario: Removed queue disappears from the menus immediately
- **WHEN** the user removes a queue
- **THEN** that queue is no longer listed in the "Start queue" / "Stop queue" toolbar dropdowns

### Requirement: Queue cards are collapsible with a collapse/expand-all control
Each queue card SHALL be collapsible so its item list can be hidden while its header (name and aggregate stats) stays visible. The Queues page SHALL provide a toolbar control to collapse all or expand all queues at once.

#### Scenario: Collapse all folds every queue
- **WHEN** the user activates Collapse all
- **THEN** every queue's item list is hidden and each queue header remains visible with its aggregate stats

#### Scenario: Expand all unfolds every queue
- **WHEN** the user activates Expand all
- **THEN** every queue's item list is shown

#### Scenario: A single queue collapses independently
- **WHEN** the user collapses one queue
- **THEN** only that queue's item list hides; other queues are unaffected

### Requirement: Queue header controls have clear clickable affordance
The per-queue header SHALL group interactive controls (run/pause toggle, concurrency cap, queue actions) with spacing/dividers and button chrome so users can distinguish clickable controls from static labels.

#### Scenario: Clickable controls are distinguishable
- **WHEN** the user views a queue header
- **THEN** actionable controls are visually grouped and spaced apart from static text, so which elements are clickable is clear at a glance

### Requirement: Large queues render without blocking
The Queues page SHALL open, expand, and close in interactive time (no multi-second UI block) regardless of item count: item rows are virtualized and only built for expanded queues.

#### Scenario: Opening with thousands of items
- **WHEN** the Queues page opens with 2000+ downloads across queues
- **THEN** the page appears without a noticeable UI freeze and scrolling stays smooth


### Requirement: Renaming a queue updates every place its name is shown
When a queue is renamed, the new name SHALL appear everywhere the app shows it — the Queues page, the "Start queue" / "Stop queue" toolbar menus, and the downloads list's queue column — without restarting the app.

#### Scenario: The toolbar menus follow a rename
- **WHEN** the user renames a queue on the Queues page
- **THEN** the "Start queue" and "Stop queue" menus list the new name and no longer list the old one

#### Scenario: The downloads list follows a rename
- **WHEN** a queue holding downloads is renamed
- **THEN** those downloads' queue column shows the new name

#### Scenario: Editing the name is not interrupted
- **WHEN** the user clears the name box and types a new name character by character
- **THEN** the text they type is left exactly as typed while editing (nothing is trimmed, reverted, or reordered under the cursor)

### Requirement: A new queue is named before it is created
The Queues page SHALL ask for the queue's name first and create the queue only once a non-blank name is given. After creating it, the page SHALL bring the new queue's card into view.

#### Scenario: Naming precedes creation
- **WHEN** the user activates "New queue"
- **THEN** a name box appears and no queue has been created yet

#### Scenario: A blank name creates nothing
- **WHEN** the user confirms with an empty name
- **THEN** no queue is created and the name box stays open

#### Scenario: Cancelling creates nothing
- **WHEN** the user cancels the name box
- **THEN** no queue is created

#### Scenario: The new card is brought into view
- **WHEN** a queue is created while the list already fills the page
- **THEN** the page scrolls so the new queue's card is visible

### Requirement: One queue is the default and the user chooses it
Each queue card SHALL offer a "default" control marking the queue that new downloads land in. Exactly one queue SHALL be the default at any time: choosing one clears the previous choice, and the current default cannot be cleared without choosing another. The choice SHALL persist across restarts.

#### Scenario: Choosing a default moves it
- **WHEN** the user marks a queue as default while another queue is the default
- **THEN** the chosen queue is the default and the previously chosen one is no longer marked

#### Scenario: The default cannot be left unset
- **WHEN** the user tries to clear the mark on the current default queue
- **THEN** the queue stays the default

#### Scenario: New downloads land in the chosen queue
- **WHEN** a download is added without naming a queue
- **THEN** it is placed in the queue marked as default

#### Scenario: Deleting the default leaves a valid default
- **WHEN** the queue marked as default is deleted
- **THEN** another existing queue is the default and downloads still have somewhere to land

#### Scenario: The choice survives a restart
- **WHEN** the user marks a queue as default and restarts the app
- **THEN** the same queue is still the default

### Requirement: The concurrency setting follows the default queue
The Settings "maximum concurrent downloads" value mirrors the default queue's cap, so when the default moves to another queue the setting SHALL show that queue's cap.

#### Scenario: Picking a new default updates the setting
- **WHEN** the user marks a queue whose cap differs from the current default's as the new default
- **THEN** the Settings concurrency value reads the newly chosen queue's cap
