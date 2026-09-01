# extension-media-relevance Specification

## Purpose

How the popup presents detected media: one list ordered by media type (no relevance guessing, no collapsed section), and an explanatory state on known-unsupported sites instead of a misleading list.
## Requirements

### Requirement: Known-unsupported sites always show an explanatory state
On a page whose hostname is known to stream via a protected/adaptive mechanism the extension cannot capture (e.g. YouTube), the popup SHALL always show a message explaining that the site is not supported and SHALL suppress the detected-media list, regardless of whether unrelated resources were incidentally sniffed.

#### Scenario: YouTube shows an explanation, not a silent blank list
- **WHEN** the popup is opened on a known-unsupported hostname and no media was detected
- **THEN** the popup displays a message explaining the site streams in a format that cannot be captured directly

#### Scenario: Incidental unrelated detections do not suppress the message
- **WHEN** the popup is opened on a known-unsupported hostname and unrelated resources were sniffed (e.g. the site's own UI sound effects)
- **THEN** the popup still shows the explanatory message and does not list those resources as downloadable

#### Scenario: Other sites keep the generic empty state
- **WHEN** the popup is opened on a hostname not in the known-unsupported list and no media was detected
- **THEN** the popup shows today's generic "No media detected on this page yet" message

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
