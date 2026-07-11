# link-variants Specification

## Purpose
TBD - created by archiving change link-variants. Update Purpose after archive.
## Requirements
### Requirement: Resolvers can enumerate selectable variants behind a link
The plugin SDK SHALL let an `ILinkResolver` optionally report the selectable variants (qualities, model tags, assets…) behind a URL via `GetVariantsAsync(url, options, ct)`, each with a stable `Id`, a user-facing `Label`, an optional `ExpectedSize`, and an `IsDefault` flag. The method SHALL be default-implemented to return null so existing and external plugins keep working unchanged.

#### Scenario: Plugin without variants is unaffected
- **WHEN** a resolver that does not override `GetVariantsAsync` is asked for variants
- **THEN** it returns null and the host resolves the link exactly as before

#### Scenario: Variant-capable resolver lists choices
- **WHEN** the HLS resolver is asked for variants of a video page URL
- **THEN** it returns one variant per distinct video height plus an "Audio only" variant, with the best quality marked `IsDefault`

### Requirement: The chosen variant drives the resolve
`ResolveOptions` SHALL carry a `VariantId`; when set, the resolver SHALL build the plan for exactly that variant. The chosen id SHALL persist on the download item so retries and restarts re-resolve the same variant.

#### Scenario: Resolve honors the selected variant
- **WHEN** a download item with `VariantId = "720"` is started
- **THEN** the resolver receives `VariantId = "720"` and the resulting plan downloads the 720p stream, not the default pick

#### Scenario: Retry keeps the user's choice
- **WHEN** a failed variant download is retried
- **THEN** the fresh resolve uses the same persisted `VariantId`

### Requirement: Add window offers a multi-select variant picker
For a single pasted URL claimed by an enabled variant-capable resolver, the Add window SHALL fetch the variants in the background and show them as a multi-select list with the default variant pre-checked. The Download action SHALL stay disabled until the variant list has loaded (or the lookup failed). Each checked variant SHALL become its own download item. Multi-URL input SHALL skip the picker.

#### Scenario: Multiple selections become multiple downloads
- **WHEN** the user checks `gemma3:4b` and `gemma3:12b` and confirms
- **THEN** two download items are added, one per selected variant

#### Scenario: Download waits for the list on variant-capable links
- **WHEN** the variant lookup for a video URL is still running
- **THEN** the Download button is disabled and a "Fetching options…" indicator shows

#### Scenario: Variant lookup failure falls back to default behavior
- **WHEN** the variant lookup throws or returns null/empty
- **THEN** the Download button enables and the add proceeds with the resolver's automatic pick (no variant)

### Requirement: Listing must not double heavy extraction
A resolver whose variant listing requires an expensive extraction (e.g. yt-dlp) SHALL reuse that extraction for the subsequent resolve of the same URL instead of running it twice.

#### Scenario: One extraction serves list and resolve
- **WHEN** variants were just listed for a YouTube URL and the user starts a selected variant
- **THEN** the resolve reuses the cached extraction result rather than re-running yt-dlp

