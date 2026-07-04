## ADDED Requirements

### Requirement: Language flag icons render at high resolution
Each language option's flag icon in the Language picker SHALL be sourced from an image with high enough resolution that it renders crisply (no visible blur or blockiness) at the picker's display size on both standard and HiDPI displays.

#### Scenario: Flag renders crisply in the language picker
- **WHEN** the user opens the Language combobox in Settings
- **THEN** each flag icon appears sharp and clean at its display size
- **AND** no flag appears blurry or pixelated on a HiDPI (2x/3x scale factor) display

#### Scenario: Flag identity is preserved
- **WHEN** a flag icon is regenerated at a higher resolution
- **THEN** it still depicts the correct country/language mapping and design details (e.g. the Persian flag stays a plain green/white/red tricolor with no emblem)
