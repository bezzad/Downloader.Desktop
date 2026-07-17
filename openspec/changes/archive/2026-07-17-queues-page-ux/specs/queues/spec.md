# queues Specification (delta)

## ADDED Requirements

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
