## ADDED Requirements

### Requirement: Removing a download can delete its partial file
Settings SHALL offer an option that makes Remove delete the download's half-finished file from disk
as well as its record. The option SHALL default to off, preserving today's behaviour. A completed
file SHALL never be deleted by Remove, whatever the option's value.

#### Scenario: Option off leaves the partial file alone
- **WHEN** the option is off and the user removes an unfinished download
- **THEN** the record is removed and the partial file is left on disk

#### Scenario: Option on deletes the partial file
- **WHEN** the option is on and the user removes an unfinished download
- **THEN** the record is removed and the partial file is deleted from disk

#### Scenario: A completed file is never deleted
- **WHEN** the option is on and the user removes a completed download
- **THEN** the record is removed and the downloaded file remains on disk
