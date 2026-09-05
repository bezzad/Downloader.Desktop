## MODIFIED Requirements

### Requirement: A server that refuses concurrent requests is retried with fewer connections

The app SHALL retry such a download against the same address with a reduced number of connections, halving
the count until the host accepts it or the attempt budget is spent, rather than retrying exactly once over a
single connection. This applies when a download fails in a way that indicates the server refused the
*request* rather than the *address* — a forbidden response while several connections were in flight. The user's configured maximum
number of connections SHALL be treated as a ceiling that a download may use, not a count every download
must use. The reduced-connection retry SHALL continue to precede the walk to a download's other addresses,
because the address that produced the refusal is the only one known to answer.

#### Scenario: A server that only tolerates a few connections still downloads
- **WHEN** a server refuses a download requested over several connections but serves it over fewer
- **THEN** the download completes

#### Scenario: The download settles at the highest accepted count
- **WHEN** a host refuses the configured count and accepts a larger-than-one count
- **THEN** the download runs at that larger count rather than at a single connection

#### Scenario: The retries are bounded
- **WHEN** every reduced count down to a single connection is also refused
- **THEN** the download fails without further attempts

### Requirement: A refusal is not reported as an expired link

The failure a user reads SHALL distinguish a link that is genuinely gone from a server that refused this
particular request, because the actions they imply differ — finding a fresh link versus using fewer
connections. A download refused for concurrency SHALL NOT be described as expired or withdrawn, and once
the app has reduced the count itself the message SHALL NOT tell the user to lower the setting by hand.

#### Scenario: A concurrency refusal names its own cause
- **WHEN** a download fails because the server refused every number of simultaneous connections offered
- **THEN** the message says the server refused the connections
- **AND** it does not tell the user the link has expired

#### Scenario: An expired link still says so
- **WHEN** a download fails because its address is gone
- **THEN** the existing expired-link message is shown unchanged
