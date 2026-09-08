## ADDED Requirements

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
