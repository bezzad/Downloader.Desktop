# window-chrome Specification (delta)

## ADDED Requirements

### Requirement: Modal chrome is refined and corner-true
Modal dialogs SHALL use a 1px accent border and their inner content SHALL be clipped to the rounded corners so no square edge overhangs the arc.

#### Scenario: Top corners match bottom corners
- **WHEN** any modal is open
- **THEN** all four corners render the same rounded arc with no square content poking through
