## ADDED Requirements

### Requirement: A hand-off names the extension's configured save folder
When the extension has a download folder configured, every add it sends to the app SHALL name that
folder, so the app saves the file there without consulting its own default and without asking the
user. When no folder is configured the add SHALL omit it, leaving today's behaviour unchanged.

#### Scenario: The configured folder reaches the app
- **WHEN** the extension sends an add for a link while a download folder is configured
- **THEN** the request to `/api/add` carries that folder as the download's `path`

#### Scenario: The dialog fallback is unaffected
- **WHEN** the extension falls back to the legacy `/add?url=` dialog endpoint
- **THEN** the link is still captured, and the app's Add dialog decides the folder as it does today
