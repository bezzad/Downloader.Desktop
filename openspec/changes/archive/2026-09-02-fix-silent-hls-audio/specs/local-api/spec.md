## ADDED Requirements

### Requirement: An add can carry a stream/quality choice

`POST /api/add` and its GET form SHALL accept an optional `variantId` naming which stream behind an
expandable link the caller wants (a rendition of an HLS master, a model's tag), and SHALL apply it to
that download only. The id belongs to the resolving plugin's own scheme; an unknown or absent id
SHALL NOT be an error — the resolver falls back to its default (best) choice, because a caller that
guessed wrong must still get a usable download.

This exists so a caller can hand over a **master** playlist plus the quality it wants instead of a
rendition URL: a rendition of a master whose audio lives in a separate group is video-only, and
downloading it directly produces a file with no sound.

A quality is not a credential, so it SHALL travel in the GET query as well as the JSON body, and it
SHALL survive a forwarded (CLI) add. The `201` response SHALL report the choice it accepted.

#### Scenario: The caller pins a quality
- **WHEN** a client adds a master playlist with `variantId` naming one of its renditions
- **THEN** the download resolves that rendition (with its audio) and the response reports the choice

#### Scenario: An unrecognised choice still downloads
- **WHEN** a client adds a link with a `variantId` the resolver does not know
- **THEN** the add succeeds and the resolver's default (best) stream is downloaded

#### Scenario: No choice given
- **WHEN** a client adds a link without `variantId`
- **THEN** the download behaves exactly as it did before this field existed
