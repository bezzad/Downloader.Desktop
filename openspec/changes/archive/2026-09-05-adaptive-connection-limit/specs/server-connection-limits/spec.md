## ADDED Requirements

### Requirement: The configured connection count is a ceiling

The number of connections in Settings SHALL be the maximum a download may use, not a count every server
must accept. A download SHALL settle at the highest number of simultaneous connections the serving host
actually accepts, up to that ceiling.

#### Scenario: A server that accepts the ceiling is unaffected
- **WHEN** a host serves a download at the configured count
- **THEN** the download uses that count
- **AND** no extra request is made and no limit is recorded

#### Scenario: A server that accepts fewer settles at the highest count it takes
- **WHEN** a host refuses the configured count but serves the download at a lower one
- **THEN** the download completes at the highest count that host accepted
- **AND** it is not reduced to a single connection when a larger count would have worked

### Requirement: A refusal steps the count down rather than collapsing it

The app SHALL retry a refused download with a smaller number of connections, halving the count each time
(for example 8 → 4 → 2 → 1) instead of dropping straight to one, when the failure indicates the server
rejected the number of simultaneous requests rather than the address.

#### Scenario: The step down stops at the first accepted count
- **WHEN** a host refuses eight connections and accepts four
- **THEN** the download runs at four
- **AND** no attempt is made at two or one

#### Scenario: A host that accepts nothing still fails once
- **WHEN** every count down to a single connection is refused
- **THEN** the download fails with the message that names the refusal
- **AND** it does not retry beyond the capped number of attempts

### Requirement: The number of attempts a download may spend is bounded

The app SHALL cap how many reduced attempts a single download may make, and SHALL NOT spend any of them on
a download that was resuming bytes it already had — each reduced attempt discards the partial file, because
a resumed download keeps the chunk layout its package was created with.

#### Scenario: A resumed download keeps its file
- **WHEN** a download that already had bytes on disk is refused
- **THEN** no step down happens and the partial file is left untouched
- **AND** the failure is handled by the link-refresh path instead

#### Scenario: The budget is finite
- **WHEN** a host refuses every count it is offered
- **THEN** the total number of attempts is bounded by the cap, whatever the ceiling was

### Requirement: A host's accepted limit is remembered and reused

The app SHALL record the connection limit a host imposed and start later downloads from that host at a
count that host is known to accept, so the refusal is not rediscovered — and the partial file not
discarded — on every download from that host.

#### Scenario: The next download starts at the known count
- **WHEN** a download from a host settled at four connections
- **AND** another download from the same host starts
- **THEN** it begins at four rather than at the configured ceiling
- **AND** it makes no refused attempt

#### Scenario: The memory survives a restart
- **WHEN** a recorded limit exists and the app is restarted
- **THEN** the limit still applies to downloads from that host

#### Scenario: A raised ceiling is still respected
- **WHEN** a recorded limit is higher than the configured count
- **THEN** the configured count wins, because it is the user's ceiling

### Requirement: A remembered limit is a hint, not a verdict

A recorded limit SHALL be re-tested rather than trusted forever, so a host that was strict once is not
permanently downloaded at a reduced count.

#### Scenario: A stale limit is retried at the ceiling
- **WHEN** a recorded limit is older than the app's re-test interval
- **THEN** the next download from that host attempts the configured count again
- **AND** the recorded limit is updated by the outcome

#### Scenario: A host that no longer refuses is released
- **WHEN** a re-test at the configured count succeeds
- **THEN** the recorded limit is cleared and later downloads use the ceiling

### Requirement: The user can see what the app decided

A download that is stepping down or running below the configured count SHALL say so, so a slower download
is explained rather than mistaken for a stall or a bug.

#### Scenario: A stepping-down download reports it
- **WHEN** a download is retried at a reduced count
- **THEN** its status says the server refused the number of connections and that fewer are being used
- **AND** it does not read as failed
