# plugins Specification (delta)

## ADDED Requirements

### Requirement: The download flow resolves links through enabled plugins

Before downloading, the application SHALL offer the pasted link to the enabled plugins' link resolvers; when a
resolver claims the link, the application SHALL download the resolver's resolved asset URL instead of the
original link, using the resolver's suggested file name when the user did not provide one.

#### Scenario: A claimed link is rewritten to the real asset

- **WHEN** a download starts for a link an enabled plugin resolver claims (e.g. `github.com/owner/repo`)
- **THEN** the engine downloads the resolver's resolved asset URL (e.g. the latest release asset for this OS)
- **AND** the suggested file name is used when the user left the name empty

#### Scenario: An unclaimed link is unchanged

- **WHEN** a download starts for a link no enabled plugin claims
- **THEN** the link is passed to the engine unchanged

#### Scenario: A resolver failure does not break the download

- **WHEN** a plugin resolver throws while resolving a link
- **THEN** the original link is used as-is and the download proceeds
