# link-refresh Specification

## Purpose
Recovering a download whose source link stopped working — automatically re-resolving an expired signed link,
and letting the user supply a fresh one — so a long transfer continues instead of losing its partial file.
## Requirements
### Requirement: An interrupted download recovers from an expired link by itself
The app SHALL automatically retry a download that has already saved part of a file when it fails because its
link is no longer valid, resolving the original link again and keeping the partial file. The number of
automatic attempts SHALL be bounded, and while they run the download SHALL NOT be reported as failed.

A download handed over by the browser extension SHALL also get one such automatic attempt when its
FIRST request fails as no-longer-valid, before any bytes exist. For those downloads "no bytes yet" does
not mean the link was never good: the browser was demonstrably able to fetch it a moment earlier, and
the usual cause is a single-use address that the browser already spent, which resolving the original
link again replaces.

A download whose link the app cannot use SHALL NOT be described to the user as an expired link the user
must replace while the browser is still downloading that same file. In that case the message SHALL say
that the app could not take the download over and that the browser is still fetching it.

#### Scenario: A resumed download whose link expired is refreshed
- **WHEN** a download with bytes already on disk fails with a status that means the link is no longer valid
- **THEN** the app resolves the original link again and continues the download from the partial file
- **AND** no failure notification is raised for that attempt

#### Scenario: A link that never worked fails immediately
- **WHEN** a download that has saved no bytes and did not come from the browser extension fails with the same status
- **THEN** it is marked failed straight away, without automatic retries

#### Scenario: An extension hand-off gets one attempt from zero bytes
- **WHEN** a download handed over by the browser extension fails on its first request with a status that means the link is no longer valid
- **THEN** the app resolves its original link again and retries once before reporting a failure

#### Scenario: A permanently dead link stops retrying
- **WHEN** the automatic attempts for a download are exhausted and it still fails
- **THEN** the download is marked failed
- **AND** the message explains that the link is no longer valid and that a fresh link can be supplied

#### Scenario: A file the user has not lost is not reported as an expired link
- **WHEN** an intercepted download fails in the app while the browser's own download of the same file is still running
- **THEN** the message says the app could not take the download over and that the browser is still fetching it
- **AND** it does not tell the user to paste a fresh link

### Requirement: The user can give an existing download a fresh link
The details view SHALL let the user replace the source link of a download that is not running and continue
it, without re-adding the download and without losing the partial file.

#### Scenario: A fresh link for the same file continues the download
- **WHEN** the user supplies a new link that reports the same size as the file being downloaded
- **THEN** the download's source link is replaced
- **AND** the download resumes from the partial file already on disk

#### Scenario: A link to a different file is confirmed first
- **WHEN** the user supplies a new link whose reported size differs from the size already known
- **THEN** the app states that the partial file will be discarded and the download will start over
- **AND** the link is replaced only if the user confirms

#### Scenario: An unusable link changes nothing
- **WHEN** the supplied link cannot be reached
- **THEN** the download keeps its previous link
- **AND** the reason is shown to the user

#### Scenario: A link that reports no size is accepted
- **WHEN** the supplied link is reachable but reports no size
- **THEN** the link is replaced and the download continues, without a confirmation

### Requirement: Every address a download was given is tried before it fails

A download that carries more than one URL SHALL attempt them in turn when a failure is of a kind a
different address could fix — the server refusing or not finding the address, or not answering at all.
Each URL SHALL lead at most one attempt, so a download can never retry unboundedly, and a download whose
leading URL works SHALL NOT contact the others.

This exists because the alternative was already shipped and was wrong: handing the app a second URL is
worthless if nothing ever requests it.

#### Scenario: The second address is the one that works
- **WHEN** a download's first URL is refused by the server and its second serves the file
- **THEN** the download completes with the file's real content, without the user doing anything

#### Scenario: A working first address is used alone
- **WHEN** a download's first URL serves the file
- **THEN** no request is made to the remaining URLs as a lead address

#### Scenario: Every address failing still fails, and only once
- **WHEN** every URL a download carries is refused
- **THEN** the download fails, having made at most one leading attempt per URL

### Requirement: A server that refuses concurrent requests is retried with one connection

The app SHALL retry such a download once using a single connection before failing it, when the failure
indicates the server refused the *request* rather than the *address* — a forbidden response while several
connections were in flight. The user's configured maximum number of connections SHALL be treated as a
ceiling that a download may use, not a count every download must use.

#### Scenario: A server that only tolerates a few connections still downloads
- **WHEN** a server refuses a download requested over several connections but serves it over one
- **THEN** the download completes

#### Scenario: The retry is not repeated
- **WHEN** the single-connection retry is also refused
- **THEN** the download fails without further attempts

### Requirement: A refusal is not reported as an expired link

The failure a user reads SHALL distinguish a link that is genuinely gone from a server that refused this
particular request, because the actions they imply differ — finding a fresh link versus lowering the
number of connections. A download refused for concurrency SHALL NOT be described as expired or withdrawn.

#### Scenario: A concurrency refusal names its own cause
- **WHEN** a download fails because the server refused several simultaneous connections
- **THEN** the message says the server refused the number of connections and names the setting to lower
- **AND** it does not tell the user the link has expired

#### Scenario: An expired link still says so
- **WHEN** a download fails because its address is gone
- **THEN** the existing expired-link message is shown unchanged

