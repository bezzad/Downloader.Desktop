## ADDED Requirements

### Requirement: Clipboard URL suggestion on empty Add dialog
When the Add dialog opens with no URL already entered and the system clipboard contains text that parses into one or more valid http/https URLs, the app SHALL display that content as a visually distinct, non-committed suggestion in the URL box, without altering the actual (bound) URL value until the user explicitly accepts it.

#### Scenario: Clipboard holds a single valid URL
- **WHEN** the Add dialog opens with an empty URL box
- **AND** the clipboard contains a single valid http/https URL
- **THEN** the URL box shows that URL as a dimmed/placeholder-style suggestion
- **AND** the dialog's `CanDownload`/parsed-URL state is unaffected until the suggestion is accepted

#### Scenario: Clipboard holds multiple valid URLs
- **WHEN** the Add dialog opens with an empty URL box
- **AND** the clipboard contains multiple valid http/https URLs separated by the same separators the dialog already accepts (newlines, spaces, tabs, commas, semicolons)
- **THEN** all of them are shown together as the suggestion, exactly as if the user had pasted them

#### Scenario: Accepting the suggestion
- **WHEN** a clipboard suggestion is showing and the user presses Enter or Tab while the URL box is still empty
- **THEN** the suggested text becomes the real URL box content
- **AND** normal parsing, validation, and single-link name/size resolution proceed as if the user had typed or pasted it

#### Scenario: No suggestion when clipboard has no valid URL
- **WHEN** the Add dialog opens with an empty URL box
- **AND** the clipboard is empty or does not parse into any valid http/https URL
- **THEN** no suggestion is shown and the dialog behaves exactly as it does today

#### Scenario: No suggestion when a URL is already present
- **WHEN** the Add dialog opens already seeded with a URL (e.g. from a browser-extension capture or a pasted link)
- **THEN** no clipboard suggestion is shown, and the existing seeded URL is left untouched

#### Scenario: Typing ignores the suggestion
- **WHEN** a clipboard suggestion is showing
- **AND** the user types or pastes different text instead of accepting it
- **THEN** the suggestion disappears and the dialog behaves exactly as it does today with the user's own input
