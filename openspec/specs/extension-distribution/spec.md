# extension-distribution Specification

## Purpose

How the browser extension reaches users: store zips attached to every GitHub Release, and version-gated automatic publishing of the Firefox build to Mozilla AMO (Chrome/Edge remain manual dashboard uploads).

## Requirements

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

### Requirement: Code changes without a version bump fail loudly
When a push changes extension code files (not documentation) while the manifest version is already published on AMO, the workflow SHALL fail with instructions to bump the version in both manifests, so changes cannot silently strand users on an old build.

#### Scenario: Forgotten bump is caught
- **WHEN** a push modifies extension code but keeps a manifest version that already exists on AMO
- **THEN** the workflow fails with an error naming the changed files and instructing to bump both manifests

#### Scenario: Doc-only edits stay green
- **WHEN** a push changes only Markdown documentation under the extension folder
- **THEN** the workflow completes successfully without failing the bump guard
