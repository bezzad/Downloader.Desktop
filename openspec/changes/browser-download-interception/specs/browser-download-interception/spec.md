## ADDED Requirements

### Requirement: The extension takes over downloads the browser starts
When interception is enabled and a download begins in the browser, the extension SHALL cancel the browser's own download and hand the link to the desktop app instead, so the file is fetched by the app's engine rather than the browser.

#### Scenario: An ordinary file link is intercepted
- **WHEN** interception is on, the app is reachable, and the user clicks a link that starts a browser download of an ordinary file
- **THEN** the browser's download is cancelled before it transfers the file
- **AND** the app receives the link and starts downloading it

#### Scenario: The user is told it was taken over
- **WHEN** a download is intercepted
- **THEN** the extension makes the takeover visible, so a vanished browser download is never unexplained

#### Scenario: Interception is off by default until the user opts in
- **WHEN** the extension is installed or updated and the user has not chosen a setting
- **THEN** browser downloads behave exactly as they did before

### Requirement: An intercepted download carries the context it was found in
A hand-off from interception SHALL include the cookies, referer and request headers needed to fetch the link, so a download the browser could fetch from an authenticated session does not fail once the app takes it over.

#### Scenario: A session-gated file survives the hand-off
- **WHEN** an intercepted download's URL requires the browser's signed-in session
- **THEN** the app receives that session's cookies and the originating page's referer
- **AND** the download succeeds in the app

#### Scenario: A context the app cannot accept is not silently lost
- **WHEN** the extension hands off a download whose context the running app version does not accept
- **THEN** the extension does not report success on the basis of a hand-off that dropped the context

### Requirement: The user controls what is intercepted
Interception SHALL be governed by user-visible settings: an on/off switch, a minimum file size below which downloads are left to the browser, a list of file types to intercept or ignore, and a list of sites to exclude. Rules SHALL be evaluated before the browser's download is cancelled.

#### Scenario: A small file is left to the browser
- **WHEN** a download's reported size is below the configured minimum
- **THEN** the browser downloads it normally and the app is not involved

#### Scenario: An excluded site is left to the browser
- **WHEN** a download originates from a site on the exclusion list
- **THEN** the browser downloads it normally

#### Scenario: A file type the user does not want intercepted is left alone
- **WHEN** a download's file type is not one the user chose to intercept
- **THEN** the browser downloads it normally

#### Scenario: Unknown size does not block interception
- **WHEN** a download's size is not known at the moment the decision is made
- **THEN** the minimum-size rule does not by itself prevent interception

### Requirement: Interception never costs the user the file
If the desktop app cannot be reached, or the hand-off fails for any reason, the browser's own download SHALL proceed. The extension SHALL NOT cancel a browser download it has not successfully handed off.

#### Scenario: App not running
- **WHEN** interception is on but the desktop app is not reachable
- **THEN** the browser download proceeds normally and nothing is lost

#### Scenario: Hand-off fails after the decision to intercept
- **WHEN** the extension attempts a hand-off and the app rejects it or does not answer
- **THEN** the download is left to the browser, or restarted in the browser, rather than cancelled with nothing to show for it

### Requirement: The extension has a settings page
The extension SHALL provide an options page, reachable from the browser's extension UI and from the popup, where the interception settings are viewed and changed. Settings SHALL persist across browser restarts.

#### Scenario: Options are reachable
- **WHEN** the user opens the extension's options from the browser's extensions page or from the popup
- **THEN** the settings page opens and shows the current interception settings

#### Scenario: A changed setting takes effect without a reinstall
- **WHEN** the user changes an interception setting
- **THEN** the next download reflects the new setting, with no reload or reinstall of the extension
