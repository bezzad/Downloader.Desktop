## ADDED Requirements

### Requirement: An intercepted download can be reviewed before it is added
When the user has chosen to review downloads rather than add them silently, an intercepted download SHALL open the app's Add dialog, pre-filled with the same url, mirrors, file name, save folder, cookies, referer and headers the silent hand-off would have carried, instead of being added and started with no dialog.

The safety rule that interception never costs the user the file SHALL hold unchanged across this path: the browser's own download SHALL NOT be cancelled until the app has confirmed it is really fetching, which cannot happen before the user has confirmed the dialog.

#### Scenario: Review mode opens the dialog for an intercepted download
- **WHEN** the extension intercepts a download while the user is in dialog mode
- **THEN** the app opens the Add dialog pre-filled with that download's details and context
- **AND** nothing is added or started until the user confirms

#### Scenario: Cancelling the dialog leaves the browser's download alone
- **WHEN** the user cancels the dialog opened for an intercepted download
- **THEN** the browser's own download is neither cancelled nor interfered with, and the user keeps the file

#### Scenario: Confirming the dialog takes the download over as usual
- **WHEN** the user confirms the dialog opened for an intercepted download
- **THEN** the extension waits for proof the app is fetching before cancelling the browser's copy, exactly as it does for a silent hand-off

#### Scenario: Silent mode interception is unchanged
- **WHEN** the user is in silent mode and the extension intercepts a download
- **THEN** the hand-off, the confirmation wait and the browser-download cancellation behave exactly as they did before this change
