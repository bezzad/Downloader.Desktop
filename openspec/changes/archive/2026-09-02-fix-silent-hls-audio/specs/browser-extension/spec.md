## ADDED Requirements

### Requirement: The quality picker hands over the manifest, not the rendition

When the popup lists the qualities it parsed out of an HLS master playlist, sending one SHALL hand the
app the **master** URL plus that quality's variant id — never the rendition's own URL. A rendition of
a master that keeps its audio in a separate `#EXT-X-MEDIA` group is video-only, so sending it made the
app download a video with no sound, with no way back to the audio (a rendition's URL does not reveal
its master's).

Each listed quality SHALL keep its own rendition URL for the extension's internal purposes (size
probing, duplicate suppression, preview matching), so this affects only what is sent.

#### Scenario: A picked quality arrives as master + choice
- **WHEN** the user picks a quality on an HLS card and presses Download
- **THEN** the app receives the master playlist's URL and the id of the picked quality
- **AND** does NOT receive the rendition's URL

#### Scenario: Send-all behaves the same
- **WHEN** the user sends every detected item at once
- **THEN** each HLS card is sent as its master plus its currently selected quality

#### Scenario: A quality does not force the cookie form
- **WHEN** a send carries a quality but no cookies, headers or referer
- **THEN** the extension keeps using the plain URL form it has always used, with the quality alongside
