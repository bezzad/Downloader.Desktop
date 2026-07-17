# link-variants Specification (delta)

## ADDED Requirements

### Requirement: Variants come only from the claiming resolver
When multiple resolvers can claim a URL, the Add dialog SHALL show variants from exactly one winner (specific resolver over fallback); no other plugin's variants may be mixed in.

#### Scenario: x.com link shows only video qualities
- **WHEN** an x.com video link is entered and both the HLS and Website plugins could claim it
- **THEN** only the HLS qualities are offered (no "Offline copy (.zip)" entry)
