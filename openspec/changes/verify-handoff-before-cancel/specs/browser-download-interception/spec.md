## MODIFIED Requirements

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
