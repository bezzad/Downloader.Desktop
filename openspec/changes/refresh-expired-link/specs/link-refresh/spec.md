# link-refresh Specification (delta)

## ADDED Requirements

### Requirement: An interrupted download recovers from an expired link by itself
The app SHALL automatically retry a download that has already saved part of a file when it fails because its
link is no longer valid, resolving the original link again and keeping the partial file. The number of
automatic attempts SHALL be bounded, and while they run the download SHALL NOT be reported as failed.

#### Scenario: A resumed download whose link expired is refreshed
- **WHEN** a download with bytes already on disk fails with a status that means the link is no longer valid
- **THEN** the app resolves the original link again and continues the download from the partial file
- **AND** no failure notification is raised for that attempt

#### Scenario: A link that never worked fails immediately
- **WHEN** a download that has saved no bytes fails with the same status
- **THEN** it is marked failed straight away, without automatic retries

#### Scenario: A permanently dead link stops retrying
- **WHEN** the automatic attempts for a download are exhausted and it still fails
- **THEN** the download is marked failed
- **AND** the message explains that the link is no longer valid and that a fresh link can be supplied

### Requirement: The user can give an existing download a fresh link
The details view SHALL let the user replace the source link of a download that is not running and continue
it, without re-adding the download and without losing the partial file.

#### Scenario: A fresh link for the same file continues the download
- **WHEN** the user supplies a new link that reports the same size as the file being downloaded
- **THEN** the download's source link is replaced
- **AND** the download resumes from the partial file already on disk

#### Scenario: A link to a different file is confirmed first
- **WHEN** the user supplies a new link whose reported size differs from the size already known
- **THEN** the app states that the partial file will be discarded and the download will start over
- **AND** the link is replaced only if the user confirms

#### Scenario: An unusable link changes nothing
- **WHEN** the supplied link cannot be reached
- **THEN** the download keeps its previous link
- **AND** the reason is shown to the user

#### Scenario: A link that reports no size is accepted
- **WHEN** the supplied link is reachable but reports no size
- **THEN** the link is replaced and the download continues, without a confirmation
