# add-download Specification (delta)

## ADDED Requirements

### Requirement: Adding many links never blocks the UI thread
Parsing, de-duplicating and validating a pasted list of URLs, and any per-URL name/size/variant resolution, SHALL run off the UI thread. The Add window SHALL open and accept input immediately, and SHALL remain responsive while a large list (thousands of links) is pasted and processed. Only the final assembled list and lightweight status flags are marshaled back to the UI thread.

#### Scenario: Pasting ~2000 links keeps the app responsive
- **WHEN** the user pastes ~2000 links into the Add window's multi-URL box
- **THEN** the window does not freeze and the UI thread is not blocked while the list is parsed and validated (the modal is usable within a normal frame, not after a multi-second stall)

#### Scenario: Bulk paste does no per-link network probing at add time
- **WHEN** a multi-URL list is pasted
- **THEN** no per-URL name/size probe is performed for the list (probing is reserved for a single-URL entry), so the add completes without network latency proportional to the list size
