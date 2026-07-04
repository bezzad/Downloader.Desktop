# extension-media-relevance Specification (delta)

## ADDED Requirements

### Requirement: Main media is distinguished from other detected media
The extension SHALL correlate sniffed media URLs with the page's currently visible `<video>` or `<audio>` element — whether actively playing or paused after loading — and present matches in a distinct "Main media" section, separate from all other detected media.

#### Scenario: Viewed video is promoted to Main media
- **WHEN** the user opens the popup on a page where a video is visible and playing
- **THEN** the media group corresponding to that video appears under "Main media"

#### Scenario: A paused-but-viewed video is still promoted
- **WHEN** a visible video has finished autoplaying and sits paused on its last frame (e.g. a typical social-media inline video)
- **THEN** the media group corresponding to that video still appears under "Main media", not "Other detected"

#### Scenario: Unrelated detections are demoted, not hidden
- **WHEN** other media (thumbnails, off-screen posts, unrelated feed items) is detected on the same page
- **THEN** it appears under a separate, collapsed "Other detected (N)" section that the user can expand, rather than being omitted

#### Scenario: Ambiguous pages can promote more than one group
- **WHEN** multiple videos on the page are simultaneously visible and playing
- **THEN** more than one group may appear under "Main media" rather than forcing a single, potentially wrong choice

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
