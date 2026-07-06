## ADDED Requirements

### Requirement: Optional plugins are discoverable from an in-app catalog

The application SHALL fetch a `plugins-catalog.json` manifest from the latest GitHub Release (the same
release-lookup used for the app's own update check) and SHALL list every catalog entry not already
installed in Settings → Plugins, visually de-emphasized relative to installed plugins, with an **Add**
action. The catalog fetch failing (offline, rate-limited, no release found) SHALL NOT block or break the
Plugins page — it SHALL simply show no catalog entries.

#### Scenario: Catalog plugin appears before install
- **WHEN** the catalog lists a plugin id not present in the local plugins folder
- **THEN** it appears in Settings → Plugins in a de-emphasized state with an Add action and no
  Disable/Remove actions

#### Scenario: Catalog unavailable degrades gracefully
- **WHEN** the catalog fetch fails for any reason
- **THEN** Settings → Plugins still renders installed (built-in and user-installed) plugins normally, with
  no catalog section shown

### Requirement: Installing a catalog plugin verifies its integrity before loading

Clicking **Add** on a catalog entry SHALL download that entry's asset, compute its sha256, and compare it
to the catalog entry's `sha256` **before** extracting or loading any file from it. On a match, the
extracted plugin SHALL be placed in the user plugins folder and loaded through the existing plugin loader,
after which it behaves as a normal user-installed (removable, disableable, non-built-in) plugin. On a
mismatch, the application SHALL discard the download, leave the plugins folder untouched, and show a clear,
retryable error — it SHALL NOT load or extract the unverified content.

#### Scenario: Successful install
- **WHEN** the user clicks Add on a catalog plugin and the downloaded asset's sha256 matches the catalog
  entry
- **THEN** the plugin is extracted into the user plugins folder, loaded, and appears with Disable/Remove
  actions like any other user-installed plugin

#### Scenario: Checksum mismatch blocks install
- **WHEN** the downloaded asset's sha256 does not match the catalog entry's `sha256`
- **THEN** the application does not extract or load any file from the download
- **AND** the user sees a friendly, retryable error
- **AND** the plugins folder is unchanged

### Requirement: Installed catalog plugins are checked for updates and only updated with consent

For each installed plugin whose id also appears in the fetched catalog, the application SHALL compare the
installed `PluginDescriptor.Version` to the catalog's version for that id. When the catalog version is
newer, the application SHALL surface a notification offering the update and SHALL NOT download, verify, or
replace the plugin's files until the user explicitly accepts. On acceptance, the same download-and-verify
gate as install (see "Installing a catalog plugin verifies its integrity before loading") SHALL apply
before the existing plugin files are unloaded and replaced.

#### Scenario: Update offered, not applied automatically
- **WHEN** an installed catalog plugin's version is older than the catalog's version for that id
- **THEN** the user is notified an update is available
- **AND** no files are downloaded or replaced until the user accepts

#### Scenario: Accepted update swaps the plugin
- **WHEN** the user accepts an offered plugin update and the downloaded asset passes checksum verification
- **THEN** the old plugin is unloaded, its files are replaced with the new version's, and it is reloaded

#### Scenario: Declined or ignored update makes no changes
- **WHEN** the user does not accept an offered plugin update
- **THEN** the currently installed version continues to run unchanged
