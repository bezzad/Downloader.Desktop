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
Before downloading, the application SHALL offer the pasted link to the enabled plugins' link resolvers;
when a resolver claims the link, the application SHALL execute the resolver's **entire** `DownloadPlan`:
every part is downloaded through the engine (honoring each part's request headers), a plan with a
post-process step is assembled into one final file by the matching plugin post-processor, and the
resolver's suggested file name is used when the user did not provide one. A resolver failure SHALL NOT
break the download — the original link is used as-is.

#### Scenario: A multi-part plan produces one final file
- **WHEN** a resolver returns a plan with multiple parts and a post-process recipe (e.g. HLS segments +
  Concat)
- **THEN** all parts are downloaded to a temporary parts location
- **AND** the plugin post-processor assembles them into the final file in the user's save folder
- **AND** the temporary parts are removed after success

#### Scenario: Single-part plain plans behave as before
- **WHEN** a resolver returns one part with no post-process
- **THEN** the download behaves exactly like a normal engine download of that URL (no parts folder)

#### Scenario: Part headers are honored
- **WHEN** a plan part carries request headers (cookies/referer)
- **THEN** the engine sends those headers when downloading that part

#### Scenario: Missing post-processor fails clearly
- **WHEN** all parts finish but no enabled plugin can process the plan's post-process step
- **THEN** the item is marked Failed with a message naming the missing processing capability

### Requirement: A plugin can be removed

The Plugins page SHALL let the user remove an installed plugin; removing it SHALL stop the plugin from
contributing immediately and SHALL delete its file from the plugins folder so it does not load again on the
next launch.

#### Scenario: Removing a plugin uninstalls it

- **WHEN** the user clicks Remove on an installed plugin
- **THEN** the plugin disappears from the Plugins list
- **AND** it no longer contributes resolvers/providers
- **AND** its file is deleted so it does not reappear after restarting the app

### Requirement: Multi-part downloads report one aggregate progress and obey controls

A running plan SHALL show a single aggregate progress on the row (byte-weighted when part sizes are
known, otherwise completed-parts of total, with a reserved tail while assembling) and SHALL respond to
the standard controls: pause stops at the current part and resume continues from it; cancel stops and
removes the temporary parts; the plan run SHALL occupy one queue slot like any other download.

#### Scenario: Pause and resume mid-plan
- **WHEN** the user pauses a running multi-part download and later resumes it
- **THEN** completed parts are not re-downloaded and the run continues from where it stopped

#### Scenario: Status reflects the phase
- **WHEN** a multi-part plan is downloading or assembling
- **THEN** the row's status text distinguishes part progress (e.g. current part of total) from the
  assembling phase

### Requirement: Multi-part plans survive an app restart

The resolved plan SHALL be persisted with the download item; after an app restart, resuming the item SHALL
continue from the first incomplete part instead of restarting the whole plan or falling back to the
original link.

#### Scenario: Restart resumes the plan
- **WHEN** the app is closed while a multi-part download is paused partway and then reopened
- **THEN** resuming downloads only the remaining parts and assembles normally

#### Scenario: Retry re-resolves a stale plan
- **WHEN** a multi-part download failed (e.g. expired segment URLs) and the user retries
- **THEN** the original link is re-resolved and the download proceeds with the fresh plan

