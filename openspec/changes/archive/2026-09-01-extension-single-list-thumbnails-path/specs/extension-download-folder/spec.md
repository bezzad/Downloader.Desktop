## ADDED Requirements

### Requirement: The extension has a download folder, prefilled from the app
The extension's settings page SHALL offer a single editable text field for the folder downloads
should be saved in. It SHALL be prefilled with the folder the desktop app is currently configured to
use, read from the app, so the user starts from a correct absolute path for their own machine rather
than an empty box. The field SHALL be plain text — the browser cannot offer a folder picker for an
arbitrary OS folder, and none is provided.

#### Scenario: The field starts at the app's own default
- **WHEN** the user opens the extension's settings page for the first time while the app is running
- **THEN** the download folder field shows the app's configured default save folder

#### Scenario: The user's edit is what is remembered
- **WHEN** the user edits the folder text and it is saved
- **THEN** that folder is used for subsequent downloads, and is not overwritten by the app's default
  on a later visit to the settings page

#### Scenario: An unreachable app leaves the field usable
- **WHEN** the settings page is opened while the app is not running and no folder has been saved yet
- **THEN** the field is empty and editable, and saving nothing leaves the app to choose the folder as
  it does today

### Requirement: Every hand-off carries the configured folder
When a download folder is configured, the extension SHALL include it in every hand-off to the app —
from the popup, the context menu, and an intercepted browser download — so the app saves there
without asking. When no folder is configured, hand-offs SHALL be unchanged from today's behaviour and
the app SHALL choose the folder itself.

#### Scenario: A popup download goes to the configured folder
- **WHEN** a folder is configured and the user clicks Download on a detected item
- **THEN** the add request sent to the app names that folder as the download's save folder

#### Scenario: An intercepted download goes to the configured folder
- **WHEN** a folder is configured and the extension takes over a download the browser started
- **THEN** the add request names that folder

#### Scenario: No configured folder changes nothing
- **WHEN** no folder is configured
- **THEN** the add request omits the folder and the app applies its own configured save folder

#### Scenario: A folder the app rejects does not silently lose the download
- **WHEN** the configured folder is not a valid absolute path and the app rejects the add
- **THEN** the extension reports the failure rather than reporting success, and an intercepted
  browser download is left alone
