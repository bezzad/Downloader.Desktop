# link-variants Specification (delta)

## MODIFIED Requirements

### Requirement: Resolvers can enumerate selectable variants behind a link

#### Scenario: Variant-capable resolver lists choices
- **WHEN** the HLS resolver is asked for variants of a master `.m3u8` playlist
- **THEN** it returns one variant per `#EXT-X-STREAM-INF` rendition (highest bandwidth marked `IsDefault`), with approximate sizes when duration is known

### Requirement: The chosen variant drives the resolve

#### Scenario: Resolve honors the selected variant
- **WHEN** a download item with `VariantId` set to a master-playlist bandwidth id is started
- **THEN** the resolver builds the segment plan from that rendition, not the default (best) pick

### Requirement: Listing must not double heavy extraction

A resolver whose variant listing requires a network fetch (HLS playlist GET) SHALL cache that playlist briefly so the subsequent resolve of the same URL does not re-download it.

#### Scenario: One fetch serves list and resolve
- **WHEN** variants were just listed for a master playlist and the user starts the default (or a selected) quality
- **THEN** the resolve reuses the cached playlist GET rather than fetching the master again
