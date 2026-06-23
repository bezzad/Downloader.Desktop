## ADDED Requirements

### Requirement: Bundled sample plugin implements the current SDK
The bundled sample plugin SHALL implement the current plugin SDK contracts (`ILinkResolver`, `DownloadPart`, and related types) so that it loads successfully instead of being rejected as "not a Downloader plugin".

#### Scenario: Installing the sample plugin succeeds
- **WHEN** the user installs the bundled sample plugin from the samples folder
- **THEN** the host loads it without a "not a Downloader plugin" error
- **AND** it appears in the Plugins list

#### Scenario: Sample plugin builds against the current Abstractions
- **WHEN** the solution is built
- **THEN** the sample plugin compiles against the current `Downloader.Desktop.Plugins.Abstractions` with no references to renamed/removed types
