# queues Specification (delta)

## ADDED Requirements

### Requirement: Large queues render without blocking
The Queues page SHALL open, expand, and close in interactive time (no multi-second UI block) regardless of item count: item rows are virtualized and only built for expanded queues.

#### Scenario: Opening with thousands of items
- **WHEN** the Queues page opens with 2000+ downloads across queues
- **THEN** the page appears without a noticeable UI freeze and scrolling stays smooth
