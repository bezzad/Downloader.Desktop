# plugins Specification (delta)

## ADDED Requirements

### Requirement: Resolvers receive the download's request context
A resolver SHALL be given the per-download request context (headers, including any referer, and any supplied cookie file) so it can fetch manifests and stamp the same context onto the parts it produces.

#### Scenario: Resolver fetches its manifest with the supplied context
- **WHEN** a download carrying per-download headers is resolved by a plugin resolver
- **THEN** the resolver's own network requests for that link send those headers

#### Scenario: Produced parts inherit the context
- **WHEN** a resolver returns a multi-part plan for a link that carried per-download headers
- **THEN** each produced part carries those headers
- **AND** the host sends them when downloading every part

#### Scenario: A part's own headers win
- **WHEN** a resolver sets a header on a part that the item's request context also sets
- **THEN** the part's value is used for that part
