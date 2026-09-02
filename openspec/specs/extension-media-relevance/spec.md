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

### Requirement: All detected media appears in one list, best copy first
The popup SHALL present every detected media group in a single list, with no relevance-based
promotion, no separate section, and no collapsed group. The list SHALL be ordered so that an HLS
master playlist comes first, and everything else is ranked by the video quality its link names
(higher first); where no quality can be read, by known size (larger first); and items with neither
last, in a deterministic order. Ordering SHALL NOT depend on the clock, on playback state, on any
hint about what the user is looking at, or on the file's type beyond the HLS rule.

A quality SHALL only be used when the link or its picker label actually names one (a height such as
`1080p`, a `1920x1080` resolution, or an unambiguous shorthand such as `4K`). A relative word such as
"HD" or "high" SHALL NOT be treated as a quality, since it names no resolution and ordering on an
invented number would be less truthful than ordering on measured size.

The popup SHALL show the quality a row was ranked on whenever that row has no quality picker of its
own, so the order of the list is explainable from looking at it.

#### Scenario: The page's video is visible without expanding anything
- **WHEN** the user opens the popup on a page where media was detected
- **THEN** every detected group is listed directly, with no "Main media" heading and no collapsed
  "Other detected" section to expand

#### Scenario: An HLS master leads
- **WHEN** both an HLS master playlist and a direct file were detected on the same page
- **THEN** the master playlist is listed first

#### Scenario: Higher quality wins over a bigger file
- **WHEN** one detected video names a higher quality than another, and the lower-quality one is the
  larger file
- **THEN** the higher-quality one is listed first

#### Scenario: Size decides when no quality is named
- **WHEN** neither of two detected items names a quality and both have been probed
- **THEN** the larger one is listed first

#### Scenario: A named quality outranks an unnamed one
- **WHEN** one detected item names a quality and another does not
- **THEN** the one naming a quality is listed first, whatever their sizes

#### Scenario: A row explains its own rank
- **WHEN** a listed row has a single option and its quality could be read
- **THEN** the row displays that quality alongside its size

#### Scenario: Items with no quality and no size come last, deterministically
- **WHEN** some items have neither a readable quality nor a probed size
- **THEN** they are listed after the rest in a stable order, and re-rendering produces the same order

#### Scenario: Ordering does not depend on playback state
- **WHEN** the popup is opened on a page whose video has finished autoplaying and sits paused
- **THEN** that video's group is listed in exactly the position it would hold while playing

### Requirement: DASH manifests are not surfaced in the popup
The popup SHALL NOT list a detected `.mpd` (MPEG-DASH) manifest, because it can neither be probed for
a size nor read for a quality, so it could only appear as a row the ordering rule can say nothing
about. This SHALL NOT remove the app's ability to download DASH: a `.mpd` link SHALL remain sendable
by pasting it into the popup's own link box or adding it in the app.

#### Scenario: A sniffed DASH manifest does not appear
- **WHEN** a page fetches a `.mpd` manifest
- **THEN** it is not listed in the popup and does not count toward the toolbar badge

#### Scenario: A pasted DASH link is still sent
- **WHEN** the user pastes a `.mpd` URL into the popup's link box and sends it
- **THEN** the link is handed to the app exactly like any other pasted link
