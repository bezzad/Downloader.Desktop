## ADDED Requirements

### Requirement: Expanded panel is sized to its content
The notch overlay's expanded panel SHALL be sized to fit its actual content — the header row, up to the
top-3 running/paused downloads (name/status line + progress bar each), and a small margin — rather than a
fixed size that leaves empty space below the content.

#### Scenario: Expanded panel with 3 running downloads
- **WHEN** the overlay expands with 3 (or more, capped display at 3) active downloads
- **THEN** the panel's height is sized to the header plus the 3 displayed rows plus padding, with no
  significant empty space below the last row

#### Scenario: Expanded panel with fewer than 3 downloads or none
- **WHEN** the overlay expands with 0-2 active downloads
- **THEN** the panel does not reserve extra vertical space for rows that aren't shown
