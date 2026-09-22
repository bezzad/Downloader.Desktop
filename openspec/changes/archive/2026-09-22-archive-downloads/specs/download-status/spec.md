## MODIFIED Requirements

### Requirement: Stopped items appear under All
User-stopped downloads SHALL be visible under the All filter, unless they have been archived — an archived download appears under no status filter and is reached only through the Archived filter.

#### Scenario: Stopped item under All
- **WHEN** a download is stopped by the user and the All filter is selected
- **THEN** the stopped download is shown in the list

#### Scenario: Archived stopped item is not under All
- **WHEN** a stopped download is archived and the All filter is selected
- **THEN** the download is not shown in the list
