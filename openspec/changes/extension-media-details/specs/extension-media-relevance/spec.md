# extension-media-relevance Specification (delta)

## ADDED Requirements

### Requirement: Main media is distinguished from other detected media
The extension SHALL correlate sniffed media URLs with the page's currently visible/playing `<video>` or `<audio>` element and present matches in a distinct "Main media" section, separate from all other detected media.

#### Scenario: Viewed video is promoted to Main media
- **WHEN** the user opens the popup on a page where a video is visible and playing
- **THEN** the media group corresponding to that video appears under "Main media"

#### Scenario: Unrelated detections are demoted, not hidden
- **WHEN** other media (thumbnails, off-screen posts, unrelated feed items) is detected on the same page
- **THEN** it appears under a separate, collapsed "Other detected (N)" section that the user can expand, rather than being omitted

#### Scenario: Ambiguous pages can promote more than one group
- **WHEN** multiple videos on the page are simultaneously visible and playing
- **THEN** more than one group may appear under "Main media" rather than forcing a single, potentially wrong choice

### Requirement: Known-unsupported sites show an explanatory empty state
When no eligible media is detected on a page whose hostname is known to stream via a protected/adaptive mechanism the extension cannot capture (e.g. YouTube), the popup SHALL show a message explaining that the site is not supported, instead of the generic empty-state message.

#### Scenario: YouTube shows an explanation, not a silent blank list
- **WHEN** the popup is opened on a known-unsupported hostname and no media was detected
- **THEN** the popup displays a message explaining the site streams in a format that cannot be captured directly

#### Scenario: Other sites keep the generic empty state
- **WHEN** the popup is opened on a hostname not in the known-unsupported list and no media was detected
- **THEN** the popup shows today's generic "No media detected on this page yet" message

#### Scenario: Known-unsupported hostname with actual detections is unaffected
- **WHEN** media is detected on a known-unsupported hostname (e.g. a downloadable asset embedded elsewhere on the page)
- **THEN** the detected media is shown normally and no unsupported-site message is displayed
