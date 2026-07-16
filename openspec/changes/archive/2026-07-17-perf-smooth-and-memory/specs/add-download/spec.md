# add-download Specification (delta)

## ADDED Requirements

### Requirement: A large paste stays responsive (summary, not full render)
When a large list of URLs is entered, the Add modal SHALL represent it as a compact summary ("N links ready to add") with a Clear action, instead of rendering every line in an editable text box — so no large text layout is built and the UI does not freeze. Small lists SHALL still be shown in the normal editable box. Pasting a large list (Ctrl+V) into a URL box SHALL NOT cause the box to lay out all pasted lines.

#### Scenario: Opening the modal prefilled with thousands of links stays responsive
- **WHEN** the Add modal is opened with a very large seeded URL list (e.g. ~2000 links)
- **THEN** it shows a summary of the link count with a Clear action and does not render all lines in an editable box

#### Scenario: A large list still adds every link
- **WHEN** the user confirms the add with a large bulk list
- **THEN** one download item is created per link

#### Scenario: Clearing a bulk list returns to the normal editable box
- **WHEN** the user clears a bulk list
- **THEN** the input is emptied and the normal editable URL box is shown again

### Requirement: Bulk add does no per-link network probing
When more than one URL is entered, the Add window SHALL NOT perform per-URL name/size/variant resolution (probing is reserved for a single-URL entry), so adding many links does not incur network latency proportional to the list size.

#### Scenario: Pasting many links performs no per-link probe
- **WHEN** a multi-URL list is entered
- **THEN** no per-URL name/size/variant probe is performed for the list
