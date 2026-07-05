## ADDED Requirements

### Requirement: Create a queue from the Add dialog
The Add Download dialog SHALL offer an "Add queue" action (styled like the Add button, placed to its
left) that creates a new named queue inline, refreshes the dialog's queue picker, and selects the new
queue so the download(s) being added are assigned to it.

#### Scenario: New queue created and used
- **WHEN** the user clicks "Add queue" in the Add dialog, enters a name and confirms
- **THEN** the queue is created through the app's queue system (visible on the Queues page)
- **AND** the dialog's queue picker appears/refreshes with the new queue selected
- **AND** starting the download assigns it to that queue

#### Scenario: Empty name is ignored
- **WHEN** the user confirms an empty/whitespace queue name
- **THEN** no queue is created and the dialog stays usable
