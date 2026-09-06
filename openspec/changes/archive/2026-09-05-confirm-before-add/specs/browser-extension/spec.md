## ADDED Requirements

### Requirement: The add-mode choice governs every hand-off
The extension's silent-vs-dialog choice SHALL apply to **every** link the extension hands to the app — popup and context-menu captures, intercepted browser downloads, and any other capture path — not only to the paths that call the ordinary capture helper. In dialog mode a hand-off SHALL be sent to `/api/add` with `confirm: true` rather than through the legacy URL-only endpoint, so the dialog opens carrying the link's cookies, referer, headers, mirrors and save folder.

A hand-off that carries an explicitly picked stream/quality SHALL remain silent regardless of the choice, because the dialog would discard the pick the user just made.

#### Scenario: Dialog mode is honoured on an intercepted download
- **WHEN** the user has set the popup toggle to "Open dialog" and the extension intercepts a browser download
- **THEN** the hand-off request sets `confirm: true` and the app opens the Add dialog

#### Scenario: Dialog mode keeps the hand-off's context
- **WHEN** a hand-off is sent in dialog mode for a link that has cookies and a referer
- **THEN** the request still carries those cookies, that referer and its headers, mirrors and save folder

#### Scenario: Silent mode is unchanged
- **WHEN** the user leaves the popup toggle on "Add silently"
- **THEN** every hand-off is sent without `confirm` and the app adds it silently, exactly as before

#### Scenario: An explicit quality pick stays silent
- **WHEN** the user picks a specific quality from the extension's picker while in dialog mode
- **THEN** that hand-off is sent silently so the picked variant is not lost

### Requirement: A dialog-mode hand-off waits for the user's answer
When a hand-off is sent in dialog mode, the extension SHALL follow the `ticket` the app returns via `/api/add-status` and treat the hand-off as successful only once the app reports the download was actually added. A cancelled dialog, an unknown ticket, or no answer within a bounded time SHALL be reported to the caller as a hand-off that did not happen.

#### Scenario: A confirmed dialog completes the hand-off
- **WHEN** the app answers `202` with a ticket and the user then confirms the dialog
- **THEN** the extension reads the item `id` from the ticket and proceeds exactly as it does after a silent add

#### Scenario: A cancelled dialog is not a successful hand-off
- **WHEN** the user cancels the dialog opened by a hand-off
- **THEN** the extension reports the hand-off as not taken

#### Scenario: An unanswered dialog does not hang the extension
- **WHEN** the ticket is still pending after the extension's wait budget
- **THEN** the extension stops waiting and reports the hand-off as not taken
