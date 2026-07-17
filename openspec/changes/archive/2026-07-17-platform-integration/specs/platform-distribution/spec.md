# platform-distribution Specification (delta)

## ADDED Requirements

### Requirement: The app is installable from the AUR on Arch-based distros
The app SHALL provide an AUR package (`downloader-bin`) that repacks the released Linux x64 build, so Arch users can install it via an AUR helper (e.g. `yay -S downloader-bin`). The release process SHALL update the AUR package as part of `release.sh` (pushing to the AUR requires the maintainer's configured AUR SSH credentials).

#### Scenario: PKGBUILD tracks the released version
- **WHEN** a version is released
- **THEN** the AUR `PKGBUILD`/`.SRCINFO` reference that version and the released asset's checksum

#### Scenario: Release automation updates the AUR
- **WHEN** `release.sh` runs with AUR credentials configured
- **THEN** it updates and pushes the `downloader-bin` AUR package; without credentials it reports the step is skipped
