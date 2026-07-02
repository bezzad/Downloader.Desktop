# plugins Specification

## Purpose

Loading expectations for the bundled sample plugin against the current plugin SDK.
## Requirements
### Requirement: Bundled sample plugin implements the current SDK
The bundled sample plugin SHALL implement the current plugin SDK contracts (`ILinkResolver`, `DownloadPart`, and related types) so that it loads successfully instead of being rejected as "not a Downloader plugin".

#### Scenario: Installing the sample plugin succeeds
- **WHEN** the user installs the bundled sample plugin from the samples folder
- **THEN** the host loads it without a "not a Downloader plugin" error
- **AND** it appears in the Plugins list

#### Scenario: Sample plugin builds against the current Abstractions
- **WHEN** the solution is built
- **THEN** the sample plugin compiles against the current `Downloader.Desktop.Plugins.Abstractions` with no references to renamed/removed types

### Requirement: The download flow resolves links through enabled plugins
Before downloading, the application SHALL offer the pasted link to the enabled plugins' link resolvers; when a resolver claims the link, the application SHALL download the resolver's resolved asset URL instead of the original link, using the resolver's suggested file name when the user did not provide one.

#### Scenario: A claimed link is rewritten to the real asset
- **WHEN** a download starts for a link an enabled plugin resolver claims (e.g. `github.com/owner/repo`)
- **THEN** the engine downloads the resolver's resolved asset URL (e.g. the latest release asset for this OS)
- **AND** the suggested file name is used when the user left the name empty

#### Scenario: An unclaimed link is unchanged
- **WHEN** a download starts for a link no enabled plugin claims
- **THEN** the link is passed to the engine unchanged

#### Scenario: A resolver failure does not break the download
- **WHEN** a plugin resolver throws while resolving a link
- **THEN** the original link is used as-is and the download proceeds

### Requirement: A plugin can be removed

The Plugins page SHALL let the user remove an installed plugin; removing it SHALL stop the plugin from
contributing immediately and SHALL delete its file from the plugins folder so it does not load again on the
next launch.

#### Scenario: Removing a plugin uninstalls it

- **WHEN** the user clicks Remove on an installed plugin
- **THEN** the plugin disappears from the Plugins list
- **AND** it no longer contributes resolvers/providers
- **AND** its file is deleted so it does not reappear after restarting the app

