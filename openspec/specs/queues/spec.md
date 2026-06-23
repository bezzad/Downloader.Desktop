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
