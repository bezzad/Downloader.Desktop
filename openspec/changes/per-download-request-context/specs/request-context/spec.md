# request-context Specification (delta)

## ADDED Requirements

### Requirement: A download carries its own cookies, headers and referer
Each download SHALL be able to carry its own cookies, headers and referer, applied to every request made for it. Where a per-download value and the corresponding global setting both exist, the per-download value SHALL win.

#### Scenario: Per-download values are applied
- **WHEN** a download that carries cookies, headers and a referer is started
- **THEN** its requests send those cookies, those headers and that referer

#### Scenario: Per-download referer overrides the global setting
- **WHEN** a download carries a referer and the app also has a global referer configured
- **THEN** the download's own referer is sent

#### Scenario: Downloads without a context are unaffected
- **WHEN** a download that carries no request context is started
- **THEN** it uses the app's global request settings exactly as before

### Requirement: Credentials in a request context are never persisted
Cookies and headers attached to a download SHALL be held in memory only for the current session: never written to the app's configuration file and never written to the log. A referer MAY be persisted, as it is not a credential.

#### Scenario: Restart drops the credentials
- **WHEN** the app is restarted after adding a download that carried cookies and headers
- **THEN** the saved configuration contains neither the cookies nor the headers
- **AND** the download's referer is still present

#### Scenario: Nothing is logged
- **WHEN** a download with cookies and headers is started, succeeds or fails
- **THEN** no cookie or header value appears in the app log
