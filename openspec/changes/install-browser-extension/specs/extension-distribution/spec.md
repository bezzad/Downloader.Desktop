## ADDED Requirements

### Requirement: A verifiable extension catalog is published with every release
The release workflow SHALL publish, alongside the extension zips it already attaches, a catalog asset that
lists for each browser target the version, the asset name, the checksum of that exact asset, the minimum
application version required, and optionally the published store listing URL for that target. The catalog
SHALL be generated from the built zips so it cannot disagree with them.

#### Scenario: Catalog accompanies the zips
- **WHEN** a `v*` tag is pushed and the release workflow completes
- **THEN** the GitHub Release lists the extension catalog asset alongside the Chrome/Edge and Firefox zips

#### Scenario: Catalog checksums match the published assets
- **WHEN** the catalog is generated
- **THEN** each entry's checksum is computed from the zip attached to that same release

#### Scenario: A store URL is data, not code
- **WHEN** a store listing is published for a target and its URL is added to the catalog source
- **THEN** the app offers the store path for that target without an application code change

### Requirement: The two extension manifests carry the same version
The Chrome/Edge manifest and the Firefox manifest SHALL declare the same extension version, and a test
SHALL fail when they differ, so a change published to one store cannot silently strand users of the other.

#### Scenario: Drift fails the suite
- **WHEN** the two manifests declare different versions
- **THEN** the test suite fails, naming both versions
