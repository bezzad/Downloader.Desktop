## MODIFIED Requirements

### Requirement: Multi-part downloads report one aggregate progress and obey controls

A running plan SHALL show a single aggregate progress on the row (byte-weighted when part sizes are
known, otherwise completed-parts of total, with a reserved tail while assembling) and SHALL respond to
the standard controls: pause stops at the current part and resume continues from it; cancel stops and
removes the temporary parts; the plan run SHALL occupy one queue slot like any other download.

Pausing SHALL halt **every** part that is in flight, not only the most recently started one, and while
a plan is paused the runner SHALL start no further parts. A paused plan SHALL consume no bandwidth,
and the row's displayed progress SHALL remain accurate for the bytes actually fetched.

#### Scenario: Pause and resume mid-plan
- **WHEN** the user pauses a running multi-part download and later resumes it
- **THEN** completed parts are not re-downloaded and the run continues from where it stopped

#### Scenario: Pause halts every parallel part
- **WHEN** the user pauses a multi-part download that is fetching several segments in parallel
- **THEN** all of those segments stop transferring
- **AND** no further segment is started until the download is resumed

#### Scenario: A paused plan does not silently keep downloading
- **WHEN** a multi-part download has been paused
- **THEN** the row does not continue to accumulate bytes behind a frozen progress display

#### Scenario: Status reflects the phase
- **WHEN** a multi-part plan is downloading or assembling
- **THEN** the row's status text distinguishes part progress (e.g. current part of total) from the
  assembling phase
