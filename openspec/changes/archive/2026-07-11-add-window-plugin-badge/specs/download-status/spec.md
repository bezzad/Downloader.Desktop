# download-status Specification (delta)

## ADDED Requirements

### Requirement: Downloads of page-like URLs are not flagged as expired links
The expired-link detection (a completed download that is small and looks like markup) SHALL NOT apply when the download's own URL looks like a web page (no path extension or an HTML-ish one) — HTML output is the expected content of such a URL, so the row completes normally. URLs with real file extensions keep the protection.

#### Scenario: A pasted docs page downloads successfully
- **WHEN** the user downloads `https://host/docs/` and the server returns a small HTML document
- **THEN** the item is marked Completed, not Failed with "link expired"

#### Scenario: An expired signed file link is still caught
- **WHEN** a resumed `https://cdn/file.zip?token=…` download completes tiny and contains an HTML error page
- **THEN** the item is still marked Failed with the expired-link message
