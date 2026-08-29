## MODIFIED Requirements

### Requirement: The user controls what is intercepted
Interception SHALL be governed by user-visible settings: an on/off switch, a minimum file size below
which downloads are left to the browser, a list of file types to intercept or ignore, and a list of
sites to exclude. Rules SHALL be evaluated before the browser's download is cancelled.

A download's file type SHALL be determined from what the file actually is, not from whether an
extension happens to appear in the URL path. The type SHALL be resolved from the browser's suggested
filename where the browser provides one, from the filename advertised in the response's
content-disposition metadata (whether that metadata arrives as a response header or is carried in the
URL's query string, as signed CDN links do), and from the reported MIME type. A download SHALL only be
judged as being of unknown type once none of those sources identify it.

No single source SHALL be able to mask the others: the decision SHALL consider every candidate type
the download's sources name, and SHALL intercept when any of them is a type the user chose. In
particular, a trailing dotted run in the URL path that is not a plausible file extension — a package
name such as `com.instagram.android`, a version, a bare host-like segment — SHALL NOT be treated as
the download's type, and SHALL NOT prevent a later source from identifying it.

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

#### Scenario: A package name in the path does not masquerade as a file type
- **WHEN** a download's URL path ends in a dotted segment that is not a plausible file extension, such
  as `https://d.apkpure.com/b/XAPK/com.instagram.android?version=latest`
- **THEN** that segment is not treated as the download's type
- **AND** the download is still intercepted if any other source — the response's content-disposition
  filename or the reported MIME type — names a type the user chose

#### Scenario: A genuinely unidentifiable download is left to the browser
- **WHEN** no source identifies the download's file type and the user's rules list which types to
  intercept
- **THEN** the browser downloads it normally, and the reason recorded for the decision distinguishes
  "type could not be determined" from "type is not one the user wants"

### Requirement: An intercepted download carries the context it was found in
A hand-off from interception SHALL include the cookies, referer and request headers needed to fetch the link, so a download the browser could fetch from an authenticated session does not fail once the app takes it over. That context SHALL also include the browser's own User-Agent, so the app's request resembles the request the browser was about to make and a server that checks the client identity does not refuse it.

The hand-off SHALL give the app the link the browser was asked to fetch — the one the user's click
started — as the download's primary link, and the link the browser's redirect chain ended on as a
fallback. The end of a redirect chain is frequently a signed, single-use address the browser has
already spent, whereas the starting link can be resolved again to a freshly signed one; handing over
only the spent address makes the app's very first request fail.

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

#### Scenario: A redirected download hands over the link that can be resolved again
- **WHEN** the browser's download followed a redirect chain from the clicked link to a signed address
- **THEN** the app receives the clicked link as the download's primary link
- **AND** the signed address as a fallback link, so a chain that cannot be walked again is still tried

## ADDED Requirements

### Requirement: The extension says when it cannot find the app
When the extension cannot reach the desktop app, it SHALL say so where the user is looking — naming
that it probed the app's local ports and finding nothing — rather than failing silently. Silence is
indistinguishable from a bug in the extension, and leaves a user with no way to tell that the app is
simply not running or is listening elsewhere.

#### Scenario: The popup states the app was not found
- **WHEN** the user opens the popup and the app does not answer on any of its local ports
- **THEN** the popup states that Downloader was not found, and which ports were tried

#### Scenario: A recovered app clears the message
- **WHEN** the app becomes reachable again and the popup is reopened
- **THEN** the message is gone and the popup shows its normal state
