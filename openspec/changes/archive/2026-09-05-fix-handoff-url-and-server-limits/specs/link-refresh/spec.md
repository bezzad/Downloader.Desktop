## ADDED Requirements

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
