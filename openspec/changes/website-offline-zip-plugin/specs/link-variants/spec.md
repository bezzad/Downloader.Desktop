# link-variants Specification (delta)

## ADDED Requirements

### Requirement: Variant offers merge across all claiming resolvers
When multiple enabled resolvers claim the same URL, the host SHALL collect variants from all of them (non-fallback resolvers first) and present the merged list in the Add window's picker. The first resolver's default marking SHALL win; a later resolver's variants join the list unchecked. A failing resolver's variant lookup SHALL NOT suppress the others' variants.

#### Scenario: Specific and fallback variants appear together
- **WHEN** a video page URL is claimed by the HLS resolver (quality variants) and by a fallback resolver offering an offline-copy variant
- **THEN** the picker shows the quality variants with the HLS default pre-checked plus the offline-copy variant unchecked

#### Scenario: One failing lookup does not hide the rest
- **WHEN** one claiming resolver's variant lookup throws and another returns variants
- **THEN** the picker shows the successful resolver's variants
