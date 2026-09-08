## ADDED Requirements

### Requirement: Adding several links offers a queue to keep them together
When more than one link is entered in the Add window, the app SHALL offer to create a queue for them, pre-filling a suggested name so the user only has to confirm. The suggestion SHALL be derived from the part the links' file names have in common; when the links carry no file name to compare, a plain "new queue" name SHALL be offered instead. Declining the offer SHALL leave the download(s) in the selected queue and SHALL NOT re-offer it for the rest of that dialog.

#### Scenario: A batch of episodes is named after what they share
- **WHEN** the user pastes links whose file names are `The.X.Movie.S01.E02.mkv`, `The.X.Movie.S01.E03.mkv` and `The.X.Movie.S01.E04.mkv`
- **THEN** the queue-name box is shown pre-filled with `The.X.Movie`

#### Scenario: Links with nothing in common get a plain name
- **WHEN** the user pastes links whose file names share nothing, or links that name no file at all (page addresses)
- **THEN** the queue-name box is pre-filled with the plain "new queue" name

#### Scenario: Confirming puts the batch in the new queue
- **WHEN** the user confirms the suggested name
- **THEN** the queue is created and every download being added is placed in it

#### Scenario: Declining is remembered
- **WHEN** the user cancels the offer and then pastes another batch of links in the same dialog
- **THEN** the offer is not shown again and the downloads go to the selected queue

#### Scenario: A single link is not a batch
- **WHEN** only one link is entered
- **THEN** no queue name is suggested
