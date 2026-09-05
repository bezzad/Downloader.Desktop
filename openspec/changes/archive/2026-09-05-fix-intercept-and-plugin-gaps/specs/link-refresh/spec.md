## MODIFIED Requirements

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
