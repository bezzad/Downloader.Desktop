## MODIFIED Requirements

### Requirement: Known-unsupported sites always show an explanatory state
The popup SHALL always show a message explaining that the site is not supported, and SHALL suppress the
detected-media list, on a page whose hostname streams via a protected/adaptive mechanism the extension
cannot capture and which the desktop app cannot handle either — regardless of whether unrelated resources
were incidentally sniffed.

That state SHALL reflect what the app can actually do, not a fixed list: when the running app reports
that it can extract media from the page's site — because the user installed the plugin that does so —
the popup SHALL instead offer the page to the app, carrying the page's signed-in session, and SHALL NOT
claim the site is unsupported.

#### Scenario: YouTube shows an explanation, not a silent blank list
- **WHEN** the popup is opened on a hostname the app cannot handle and no media was detected
- **THEN** the popup displays a message explaining the site streams in a format that cannot be captured directly

#### Scenario: Incidental unrelated detections do not suppress the message
- **WHEN** the popup is opened on a hostname the app cannot handle and unrelated resources were sniffed (e.g. the site's own UI sound effects)
- **THEN** the popup still shows the explanatory message and does not list those resources as downloadable

#### Scenario: Other sites keep the generic empty state
- **WHEN** the popup is opened on a hostname that is neither unsupported nor extractable and no media was detected
- **THEN** the popup shows today's generic "No media detected on this page yet" message

#### Scenario: An extractable site is offered to the app instead
- **WHEN** the popup is opened on a site the running app reports it can extract, such as YouTube with the site-extraction plugin installed
- **THEN** the popup offers to send the page to the app rather than saying the site is unsupported
- **AND** sending it hands over the page's cookies, so the app fetches it as the signed-in user

#### Scenario: The explanation names the real reason
- **WHEN** the app cannot extract a site's media because the plugin that does so is not installed
- **THEN** the message says so and points at the plugin
- **AND** it does not tell the user to sign in, when signing in is not what is missing
