# browser-download-interception Specification

## Purpose
Taking over downloads the browser starts and giving them to the desktop app instead — when to intercept,
what to leave alone, what happens when the app is unreachable, and the user's control over all of it. The
governing constraint is that interception must never cost the user a file: a download is cancelled only
once the app has accepted it.

## Requirements
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
A hand-off from interception SHALL include the cookies, referer and request headers needed to fetch the link, so a download the browser could fetch from an authenticated session does not fail once the app takes it over. That context SHALL also include the browser's own User-Agent, so the app's request resembles the request the browser was about to make and a server that checks the client identity does not refuse it.

#### Scenario: A session-gated file survives the hand-off
- **WHEN** an intercepted download's URL requires the browser's signed-in session
- **THEN** the app receives that session's cookies and the originating page's referer
- **AND** the download succeeds in the app

#### Scenario: A context the app cannot accept is not silently lost
- **WHEN** the extension hands off a download whose context the running app version does not accept
- **THEN** the extension does not report success on the basis of a hand-off that dropped the context

#### Scenario: A server that checks the client identity
- **WHEN** a server refuses requests that do not present the browser's User-Agent
- **THEN** the hand-off carries that User-Agent, so the app's request is not refused for that reason

### Requirement: The user controls what is intercepted
Interception SHALL be governed by user-visible settings: an on/off switch, a minimum file size below
which downloads are left to the browser, a list of file types to intercept or ignore, and a list of
sites to exclude. Rules SHALL be evaluated before the browser's download is cancelled.

A download's file type SHALL be determined from what the file actually is, not from whether an
extension happens to appear in the URL path. The type SHALL be resolved from the browser's suggested
filename where the browser provides one, from the filename advertised in the response's
content-disposition metadata (including when that metadata is carried in the URL's query string, as
signed CDN links do), and from the reported MIME type. A download SHALL only be judged as being of
unknown type once none of those sources identify it.

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

#### Scenario: A signed link with no extension in its path is still matched by type
- **WHEN** a download's URL path carries no file extension, but the browser's suggested filename or
  the response's content-disposition names a file whose type the user chose to intercept
- **THEN** the download is intercepted, exactly as it would be for a direct link ending in that
  extension

#### Scenario: The MIME type identifies a download nothing else names
- **WHEN** neither the URL path, the browser's suggested filename, nor the content-disposition
  identify the file type, but the reported MIME type corresponds to a type the user chose to
  intercept
- **THEN** the download is intercepted

#### Scenario: A genuinely unidentifiable download is left to the browser
- **WHEN** no source identifies the download's file type and the user's rules list which types to
  intercept
- **THEN** the browser downloads it normally, and the reason recorded for the decision distinguishes
  "type could not be determined" from "type is not one the user wants"

### Requirement: Interception never costs the user the file
If the desktop app cannot be reached, or the hand-off fails for any reason, the browser's own
download SHALL proceed. The extension SHALL NOT cancel a browser download it has not successfully
handed off.

The app accepting a download for queueing SHALL NOT by itself be treated as a successful hand-off:
acceptance means the app intends to fetch the link, not that the link is fetchable. The extension
SHALL cancel the browser's download only once the app's transfer is confirmed to have reached the
server. If the app reports the transfer failed, or the transfer is not confirmed within a bounded
wait, the browser's download SHALL be left to run and the user SHALL be told the app did not take it.

#### Scenario: App not running
- **WHEN** interception is on but the desktop app is not reachable
- **THEN** the browser download proceeds normally and nothing is lost

#### Scenario: Hand-off fails after the decision to intercept
- **WHEN** the extension attempts a hand-off and the app rejects it or does not answer
- **THEN** the download is left to the browser, or restarted in the browser, rather than cancelled with nothing to show for it

#### Scenario: The app accepts the link but cannot fetch it
- **WHEN** the app accepts the download and then its own request to the link fails, for example
  because the link was single-use and the browser already spent it, or the server refuses the app's
  request
- **THEN** the browser's download is never cancelled, the user keeps the file, and the user is told
  the app could not take it

#### Scenario: The app confirms it is fetching the link
- **WHEN** the app reports that its transfer has reached the server, by reporting a known total size
  or bytes received
- **THEN** the browser's own download is cancelled, because the file is now genuinely being fetched
  elsewhere

#### Scenario: The app neither confirms nor fails in time
- **WHEN** the app has accepted the download but has not confirmed reaching the server within the
  bounded wait
- **THEN** the browser's download is left running rather than cancelled on an unconfirmed hand-off

### Requirement: The extension has a settings page
The extension SHALL provide an options page, reachable from the browser's extension UI and from the popup, where the interception settings are viewed and changed. Settings SHALL persist across browser restarts.

#### Scenario: Options are reachable
- **WHEN** the user opens the extension's options from the browser's extensions page or from the popup
- **THEN** the settings page opens and shows the current interception settings

#### Scenario: A changed setting takes effect without a reinstall
- **WHEN** the user changes an interception setting
- **THEN** the next download reflects the new setting, with no reload or reinstall of the extension
