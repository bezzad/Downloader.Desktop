## REMOVED Requirements

### Requirement: Main media is distinguished from other detected media

**Reason**: The promotion rule depends on a visibility hint from the content script being *fresh*
(within a few seconds) at the exact moment the popup asks the background worker. On a feed page whose
inline player has finished autoplaying — x.com being the site this is used on most — the hint is
routinely stale or lands late, so the real video is demoted and the popup opens with an empty "Main
media" section and everything hidden behind a collapsed "Other detected". A relevance guess that
hides the one item the user opened the popup for is worse than no guess at all.

**Migration**: All detected media is shown in one list, ordered by media type (see the ADDED
requirement below). Nothing is hidden or collapsed, so no item that used to be reachable becomes
unreachable. The content script's `activeMediaHint` message, the background worker's per-tab hint
store, and the `main` flag on each item are removed along with it.

## ADDED Requirements

### Requirement: All detected media appears in one type-ordered list
The popup SHALL present every detected media group in a single list, with no relevance-based
promotion, no separate section, and no collapsed group. The list SHALL be ordered by media type,
adaptive-streaming manifests first (HLS before DASH), then MP4, then other video containers, then
audio, then anything else; within one type the larger known size SHALL come first, and items whose
size is not yet known SHALL follow the probed ones in a deterministic order. The ordering SHALL NOT
depend on the clock, on playback state, or on any hint about what the user is looking at.

#### Scenario: The page's video is visible without expanding anything
- **WHEN** the user opens the popup on a page where media was detected
- **THEN** every detected group is listed directly, with no "Main media" heading and no collapsed
  "Other detected" section to expand

#### Scenario: A manifest leads the list
- **WHEN** both an HLS manifest and a direct MP4 were detected on the same page
- **THEN** the manifest is listed above the MP4

#### Scenario: Same-type items are ordered by size
- **WHEN** two detected MP4 items have both been probed and have different sizes
- **THEN** the larger one is listed first

#### Scenario: Unprobed items follow probed ones, deterministically
- **WHEN** some items of a type have been probed and others have not
- **THEN** the probed ones are listed first, the unprobed ones follow in a stable order, and
  re-rendering the same list produces the same order

#### Scenario: Ordering does not depend on playback state
- **WHEN** the popup is opened on a page whose video has finished autoplaying and sits paused
- **THEN** that video's group is listed in its type's position exactly as it would be while playing
