## ADDED Requirements

### Requirement: YouTube page URLs resolve and download, including when a signed-in session is required
Pasting a YouTube video page URL SHALL resolve to a real downloadable `DownloadPlan` and complete a
successful download, including for content that requires a signed-in session. When the app cannot obtain a
working session by reading a local browser's on-disk cookie store, it SHALL accept a live session's cookies
supplied by the browser extension for the same URL and use them instead.

#### Scenario: Public video resolves and downloads without any session
- **WHEN** a YouTube video page URL that requires no sign-in is pasted into the app
- **THEN** it resolves to a `DownloadPlan` with at least one part
- **AND** the download completes successfully

#### Scenario: Session-gated video resolves using extension-supplied cookies
- **WHEN** a YouTube video page URL that requires a signed-in session is sent from the browser extension
  together with the current session's cookies for that URL
- **AND** no local browser's on-disk cookie store can provide a working session
- **THEN** the app uses the supplied cookies to resolve and download the video successfully

#### Scenario: Cookies are never persisted beyond the attempt
- **WHEN** the app uses extension-supplied cookies to resolve a download
- **THEN** the cookie data is not written to any log
- **AND** any temporary file holding the cookies is removed after the resolve/download attempt completes,
  whether it succeeded or failed
