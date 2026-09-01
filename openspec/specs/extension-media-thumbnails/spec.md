# extension-media-thumbnails Specification

## Purpose

A visual preview on each detected-media row: a frame captured from the page's own player where the browser allows it, a documented fallback chain, and bounded, local-only capture.
## Requirements

### Requirement: A detected item shows a visual preview
Each row in the popup's detected-media list SHALL show a small preview image on its leading side, so
a user can tell which video a link is without reading a signed CDN URL. When no preview can be
obtained the row SHALL show a file-type placeholder instead, and SHALL never show a broken image or
an empty gap that shifts the row's layout.

#### Scenario: A row shows the page's video frame
- **WHEN** the popup lists an item for a video the content script could capture a frame from
- **THEN** the row shows that frame as its preview

#### Scenario: A row with no available preview still looks complete
- **WHEN** no frame, poster or page image is available for an item
- **THEN** the row shows a file-type placeholder in the same position and at the same size as a real
  preview, and the list's alignment is unchanged

### Requirement: Preview sources are tried in order of how specific they are
The extension SHALL obtain a preview from the most specific source available, in this order: a frame
captured from the on-page `<video>` element, that element's `poster` image, the page's `og:image` or
`twitter:image`, and finally nothing (the placeholder). A source that fails SHALL fall through to the
next rather than leaving the row without a preview.

#### Scenario: A cross-origin video falls back to its poster
- **WHEN** capturing a frame from a video fails because the canvas is tainted by cross-origin content
- **THEN** the element's `poster` image is used as the preview

#### Scenario: A page with no player image falls back to the page image
- **WHEN** neither a frame nor a poster is available but the page declares an `og:image`
- **THEN** that image is used as the preview

### Requirement: Preview capture is cheap, bounded and local
A captured frame SHALL be downscaled to thumbnail dimensions and encoded as a lossy image before it
leaves the content script, SHALL be re-captured no more often than a fixed throttle allows, and SHALL
be discarded when the tab navigates or closes. Captured frames SHALL be passed only through the
extension's own messaging to its own popup — never sent to any server, and never included in a
hand-off to the desktop app.

#### Scenario: A frame is thumbnail-sized, not full resolution
- **WHEN** the content script captures a frame from a 1080p video
- **THEN** the image it reports is a downscaled thumbnail, not the full-resolution frame

#### Scenario: Previews do not survive navigation
- **WHEN** a tab navigates to a new page
- **THEN** the previews stored for that tab are discarded along with its detected media

#### Scenario: A hand-off carries no image data
- **WHEN** the user downloads an item that has a preview
- **THEN** the request sent to the desktop app contains the link and its context only, with no image
  data

### Requirement: Preview capture never blocks or breaks detection
Obtaining previews SHALL be best-effort: a failure, a refusal by the page, or a missing content
script SHALL leave the detected-media list fully functional, and SHALL NOT delay the popup's first
render.

#### Scenario: A page that blocks injection still lists its media
- **WHEN** the popup opens on a page where the content script could not run
- **THEN** the detected media is listed with placeholders, and every Download action still works

#### Scenario: The list renders before previews arrive
- **WHEN** the popup opens
- **THEN** the list is rendered immediately and previews appear in place as they become available
