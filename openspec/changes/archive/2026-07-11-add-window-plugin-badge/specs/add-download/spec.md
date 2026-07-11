# add-download Specification (delta)

## ADDED Requirements

### Requirement: The Add window names the plugin that claims the pasted link
For a single pasted URL claimed by an enabled plugin resolver, the Add window SHALL show a badge naming that plugin ("Handled by ‹name›"), updated live as the input changes, using only the resolvers' cheap synchronous claim check (no network) and the same fallback ordering as resolution. No badge SHALL be shown for unclaimed URLs (plain engine download) or multi-URL input.

#### Scenario: Claimed link shows the plugin badge
- **WHEN** the user pastes a link an enabled plugin's resolver claims (e.g. a web page URL with the Website plugin installed)
- **THEN** the Add window shows a badge with that plugin's name before the download is started

#### Scenario: Unclaimed link shows no badge
- **WHEN** the user pastes a direct file URL no resolver claims
- **THEN** no badge is shown and the add flow is unchanged
