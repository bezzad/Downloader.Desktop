# extension-distribution Specification (delta)

## ADDED Requirements

### Requirement: Extension zips attached to every GitHub Release
The release workflow SHALL build the Chrome/Edge and Firefox extension zips and attach both to the GitHub Release of every `v*` tag, alongside the application packages, without disturbing release creation or curated notes.

#### Scenario: Release carries the extension zips
- **WHEN** a `v*` tag is pushed and the release workflow completes
- **THEN** the GitHub Release lists `downloader-extension-chrome.zip` and `downloader-extension-firefox.zip` as assets

#### Scenario: Notes and app assets unaffected
- **WHEN** the extension-assets job runs
- **THEN** it attaches to the already-created release (after the app build matrix) and does not generate or overwrite release notes

### Requirement: Automatic Mozilla AMO publish on version change
A workflow SHALL trigger on pushes touching the extension source and submit the Firefox build to the existing AMO listing via Mozilla's submission API exactly once per manifest version; pushes that do not change the version SHALL be skipped successfully. Chrome/Edge stores SHALL NOT be automated.

#### Scenario: New version is submitted
- **WHEN** a push changes `manifest.firefox.json` to a version not yet on AMO
- **THEN** the workflow builds the Firefox extension and submits it to AMO on the listed channel for review

#### Scenario: Unchanged version is a green no-op
- **WHEN** a push touches extension files without changing the manifest version already on AMO
- **THEN** the workflow completes successfully without submitting anything

#### Scenario: Missing credentials fail soft
- **WHEN** the AMO API secrets are not configured in the repository
- **THEN** the workflow completes with a visible "skipped — secrets not configured" notice instead of failing
